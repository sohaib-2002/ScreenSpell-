using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;

namespace ScreenSpell.Engines
{
    /// <summary>
    /// PP-OCR running locally on ONNX Runtime: a DB detector finds the text lines and a CTC
    /// recogniser reads each of them. The recognition model is the Arabic PP-OCRv5 one, whose
    /// character set also covers Latin letters and digits, so a single pass reads both scripts.
    /// Unlike the Windows engine this one reports a real per word score.
    /// </summary>
    public sealed class PaddleOcrProvider : IOcrProvider, IDisposable
    {
        public const string DetectionModelFile = "paddle_det.onnx";
        public const string RecognitionModelFile = "paddle_rec_arabic.onnx";
        public const string DictionaryFile = "paddle_rec_arabic_dict.txt";

        private const int MaxDetectionSide = 2048;
        private const int RecognitionHeight = 48;
        private const int MaxRecognitionWidth = 1600;
        private const float PixelThreshold = 0.3f;
        private const float RegionThreshold = 0.5f;
        private const int MaxRegions = 256;

        private static readonly float[] Mean = { 0.485f, 0.456f, 0.406f };
        private static readonly float[] Deviation = { 0.229f, 0.224f, 0.225f };

        private readonly ILogger _logger;
        private readonly InferenceSession? _detection;
        private readonly InferenceSession? _recognition;
        private readonly string[] _labels = Array.Empty<string>();
        private readonly SemaphoreSlim _gate = new(1, 1);

        public PaddleOcrProvider(
            AppSettings? settings = null,
            ILogger<PaddleOcrProvider>? logger = null,
            string? modelDirectory = null)
        {
            _logger = logger ?? NullLogger<PaddleOcrProvider>.Instance;
            var directory = modelDirectory ?? Path.Combine(AppContext.BaseDirectory, "Models");

            var detectionPath = Path.Combine(directory, DetectionModelFile);
            var recognitionPath = Path.Combine(directory, RecognitionModelFile);
            var dictionaryPath = Path.Combine(directory, DictionaryFile);

            if (!File.Exists(detectionPath) || !File.Exists(recognitionPath) || !File.Exists(dictionaryPath))
            {
                _logger.LogWarning("PP-OCR models are missing from {Directory}; the engine stays unavailable.", directory);
                return;
            }

            try
            {
                var options = new SessionOptions
                {
                    // The scan loop is already parallel work; keep it off the whole machine.
                    IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2),
                    GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL,
                    LogSeverityLevel = OrtLoggingLevel.ORT_LOGGING_LEVEL_ERROR
                };

                _detection = new InferenceSession(detectionPath, options);
                _recognition = new InferenceSession(recognitionPath, options);
                _labels = LoadLabels(dictionaryPath);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Could not load the PP-OCR models from {Directory}.", directory);
                _detection?.Dispose();
                _recognition?.Dispose();
                _detection = null;
                _recognition = null;
            }
        }

        public bool IsAvailable => _detection is not null && _recognition is not null && _labels.Length > 0;

        public async Task<List<OcrWord>> ExtractTextAsync(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            if (!IsAvailable || frame is null)
                return new List<OcrWord>();

            await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                return await Task.Run(() => Recognise(frame, cancellationToken), cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "PP-OCR failed on a {Width}x{Height} frame.", frame.Width, frame.Height);
                return new List<OcrWord>();
            }
            finally
            {
                _gate.Release();
            }
        }

        private List<OcrWord> Recognise(ScreenFrame frame, CancellationToken cancellationToken)
        {
            var words = new List<OcrWord>();

            foreach (var region in Detect(frame, cancellationToken))
            {
                cancellationToken.ThrowIfCancellationRequested();
                words.AddRange(Read(frame, region, cancellationToken));
            }

            return words;
        }

        /// <summary>Runs the DB detector and turns its probability map into text line boxes.</summary>
        internal List<Region> Detect(ScreenFrame frame, CancellationToken cancellationToken = default)
        {
            var regions = new List<Region>();
            if (_detection is null)
                return regions;

            var scale = Math.Min(1.0, (double)MaxDetectionSide / Math.Max(frame.Width, frame.Height));
            var width = Round32(frame.Width * scale);
            var height = Round32(frame.Height * scale);
            var resized = ImageOps.Resize(frame, width, height);

            var input = new DenseTensor<float>(new[] { 1, 3, height, width });
            for (var y = 0; y < height; y++)
            {
                var row = y * resized.Stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + x * 4;
                    for (var channel = 0; channel < 3; channel++)
                    {
                        // BGRA in, RGB out.
                        var value = resized.Pixels[pixel + 2 - channel] / 255f;
                        input[0, channel, y, x] = (value - Mean[channel]) / Deviation[channel];
                    }
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var results = _detection.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_detection.InputNames[0], input)
            });

            var probabilities = results[0].AsTensor<float>().ToArray();
            var boxes = ConnectedRegions(probabilities, width, height);

            var toFrameX = (double)frame.Width / width;
            var toFrameY = (double)frame.Height / height;

            foreach (var box in boxes)
            {
                var x0 = box.Left * toFrameX;
                var y0 = box.Top * toFrameY;
                var x1 = (box.Right + 1) * toFrameX;
                var y1 = (box.Bottom + 1) * toFrameY;

                // The detector marks the ink, not the glyph box, and it clips the first and
                // last letter of a line when the crop hugs it; PP-OCR unclips the contour the
                // same way before recognition.
                var paddingX = (x1 - x0) * 0.12 + 2;
                var paddingY = (y1 - y0) * 0.35 + 2;
                regions.Add(new Region(
                    (int)Math.Max(0, x0 - paddingX),
                    (int)Math.Max(0, y0 - paddingY),
                    (int)Math.Min(frame.Width, x1 + paddingX),
                    (int)Math.Min(frame.Height, y1 + paddingY),
                    box.Score));
            }

            return regions;
        }

        /// <summary>
        /// Flood fills the thresholded probability map. PP-OCR uses contour tracing, but the
        /// boxes it derives from those contours are axis aligned anyway, which is all the
        /// underline overlay can draw.
        /// </summary>
        private static List<Region> ConnectedRegions(float[] probabilities, int width, int height)
        {
            var regions = new List<Region>();
            var visited = new bool[width * height];
            var stack = new Stack<int>();

            for (var start = 0; start < probabilities.Length; start++)
            {
                if (visited[start] || probabilities[start] <= PixelThreshold)
                    continue;

                var left = width;
                var top = height;
                var right = 0;
                var bottom = 0;
                var count = 0;
                var total = 0.0;

                visited[start] = true;
                stack.Push(start);

                while (stack.Count > 0)
                {
                    var index = stack.Pop();
                    var x = index % width;
                    var y = index / width;

                    count++;
                    total += probabilities[index];
                    if (x < left) left = x;
                    if (x > right) right = x;
                    if (y < top) top = y;
                    if (y > bottom) bottom = y;

                    for (var dy = -1; dy <= 1; dy++)
                    {
                        for (var dx = -1; dx <= 1; dx++)
                        {
                            var nx = x + dx;
                            var ny = y + dy;
                            if (nx < 0 || ny < 0 || nx >= width || ny >= height)
                                continue;

                            var neighbour = ny * width + nx;
                            if (visited[neighbour] || probabilities[neighbour] <= PixelThreshold)
                                continue;

                            visited[neighbour] = true;
                            stack.Push(neighbour);
                        }
                    }
                }

                var score = total / count;
                if (count < 12 || right - left < 3 || bottom - top < 3 || score < RegionThreshold)
                    continue;

                regions.Add(new Region(left, top, right, bottom, score));
                if (regions.Count >= MaxRegions)
                    break;
            }

            return regions;
        }

        /// <summary>Recognises one text line and splits it back into words with their boxes.</summary>
        private IEnumerable<OcrWord> Read(ScreenFrame frame, Region region, CancellationToken cancellationToken)
        {
            if (_recognition is null)
                return Array.Empty<OcrWord>();

            var crop = ImageOps.Crop(frame, region.Left, region.Top, region.Width, region.Height);
            var width = Math.Clamp(
                (int)Math.Round((double)RecognitionHeight * crop.Width / crop.Height),
                16,
                MaxRecognitionWidth);
            var line = ImageOps.Resize(crop, width, RecognitionHeight);

            var input = new DenseTensor<float>(new[] { 1, 3, RecognitionHeight, width });
            for (var y = 0; y < RecognitionHeight; y++)
            {
                var row = y * line.Stride;
                for (var x = 0; x < width; x++)
                {
                    var pixel = row + x * 4;
                    for (var channel = 0; channel < 3; channel++)
                        input[0, channel, y, x] = (line.Pixels[pixel + 2 - channel] / 255f - 0.5f) / 0.5f;
                }
            }

            cancellationToken.ThrowIfCancellationRequested();

            using var results = _recognition.Run(new[]
            {
                NamedOnnxValue.CreateFromTensor(_recognition.InputNames[0], input)
            });

            var output = results[0].AsTensor<float>();
            return Decode(output, region, frame);
        }

        /// <summary>
        /// Greedy CTC decode. The time step a character is emitted at also tells us where it
        /// sits horizontally, which is how each word gets its own box; for Arabic the model
        /// emits in reading order, so the axis is mirrored.
        /// </summary>
        private IEnumerable<OcrWord> Decode(Tensor<float> output, Region region, ScreenFrame frame)
        {
            var steps = output.Dimensions[1];
            var classes = output.Dimensions[2];

            var text = new List<char>();
            var starts = new List<int>();
            var ends = new List<int>();
            var scores = new List<float>();
            var previous = 0;

            for (var step = 0; step < steps; step++)
            {
                var best = 0;
                var bestScore = 0f;
                for (var candidate = 0; candidate < classes; candidate++)
                {
                    var score = output[0, step, candidate];
                    if (score > bestScore)
                    {
                        bestScore = score;
                        best = candidate;
                    }
                }

                if (best != 0 && best != previous && best < _labels.Length)
                {
                    foreach (var character in _labels[best])
                    {
                        text.Add(character);
                        starts.Add(step);
                        ends.Add(step + 1);
                        scores.Add(bestScore);
                    }
                }

                previous = best;
            }

            var segments = new List<(int Start, int End)>();
            var index = 0;

            while (index < text.Count)
            {
                if (char.IsWhiteSpace(text[index]))
                {
                    index++;
                    continue;
                }

                var from = index;
                while (index < text.Count && !char.IsWhiteSpace(text[index]))
                    index++;

                // The recogniser sometimes emits a space between two letters of the same
                // Arabic word (التوقيع came back as التوقي ع). A real gap between words is
                // about a quarter of the line height wide, an emitted one is a step or two.
                var gap = segments.Count > 0
                    ? (double)(starts[from] - ends[segments[^1].End - 1]) / steps * region.Width
                    : double.MaxValue;

                if (gap < region.Height * 0.3)
                    segments[^1] = (segments[^1].Start, index);
                else
                    segments.Add((from, index));
            }

            var words = new List<OcrWord>();

            foreach (var (start, end) in segments)
            {
                var characters = text.GetRange(start, end - start).Where(c => !char.IsWhiteSpace(c)).ToArray();
                if (characters.Length == 0)
                    continue;

                var word = new string(characters);
                var rightToLeft = word.Any(c => c >= 0x0600 && c <= 0x06FF);

                var first = (double)starts[start] / steps;
                var last = (double)ends[end - 1] / steps;
                var left = rightToLeft ? 1 - last : first;
                var right = rightToLeft ? 1 - first : last;

                var confidence = 0.0;
                for (var i = start; i < end; i++)
                    confidence += scores[i];

                words.Add(new OcrWord
                {
                    Text = word,
                    Confidence = confidence / (end - start),
                    BoundingBox = new BoundingBox(
                        frame.OriginX + region.Left + left * region.Width,
                        frame.OriginY + region.Top,
                        Math.Max(1, (right - left) * region.Width),
                        region.Height)
                });
            }

            return words;
        }

        /// <summary>
        /// The label list a CTC head indexes into: the blank, the characters of the model's
        /// dictionary and the space PP-OCR appends last.
        /// </summary>
        private static string[] LoadLabels(string path)
        {
            var characters = File.ReadAllLines(path);
            var labels = new List<string>(characters.Length + 2) { string.Empty };
            labels.AddRange(characters);
            labels.Add(" ");
            return labels.ToArray();
        }

        private static int Round32(double value) => Math.Max(32, (int)Math.Ceiling(value / 32) * 32);

        public void Dispose()
        {
            _detection?.Dispose();
            _recognition?.Dispose();
            _gate.Dispose();
        }

        /// <summary>A detected text line in frame pixels.</summary>
        internal readonly record struct Region(int Left, int Top, int Right, int Bottom, double Score)
        {
            public int Width => Math.Max(1, Right - Left);

            public int Height => Math.Max(1, Bottom - Top);
        }
    }
}
