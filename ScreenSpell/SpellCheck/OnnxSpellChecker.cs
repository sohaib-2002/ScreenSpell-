using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using ScreenSpell.Core.Interfaces;
using ScreenSpell.Core.Models;
using ScreenSpell.Text;

namespace ScreenSpell.SpellCheck
{
    /// <summary>
    /// Optional neural checker. It expects a character level binary classifier exported to
    /// ONNX with a single int64 input named "input_ids" of shape [1, sequence] and a float
    /// output of shape [1, 2] (index 1 = "misspelled"), plus a "vocab.txt" next to the model
    /// listing one character per line. When the model or the vocabulary is missing, or the
    /// session fails, every call is delegated to <see cref="DictionarySpellChecker"/>.
    /// </summary>
    public sealed class OnnxSpellChecker : ISpellChecker, IDisposable
    {
        private const double ErrorThreshold = 0.5;

        private readonly ISpellChecker _fallback;
        private readonly ILogger<OnnxSpellChecker> _logger;
        private readonly InferenceSession? _session;
        private readonly Dictionary<char, long> _vocabulary = new();
        private readonly string _inputName = "input_ids";

        public OnnxSpellChecker(
            ISpellChecker fallback,
            AppSettings? settings = null,
            ILogger<OnnxSpellChecker>? logger = null)
        {
            _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
            _logger = logger ?? NullLogger<OnnxSpellChecker>.Instance;

            var configured = settings?.OnnxModelPath ?? Path.Combine("Models", "ArabicSpellModel.onnx");
            var modelPath = Path.IsPathRooted(configured)
                ? configured
                : Path.Combine(AppContext.BaseDirectory, configured);

            if (!File.Exists(modelPath))
            {
                _logger.LogInformation("No ONNX model at {Path}; using the dictionary checker.", modelPath);
                return;
            }

            var vocabularyPath = Path.Combine(Path.GetDirectoryName(modelPath)!, "vocab.txt");
            if (!File.Exists(vocabularyPath))
            {
                _logger.LogWarning("Found {Model} but no vocab.txt next to it; using the dictionary checker.", modelPath);
                return;
            }

            try
            {
                _session = new InferenceSession(modelPath);
                _inputName = _session.InputMetadata.Keys.FirstOrDefault() ?? _inputName;
                LoadVocabulary(vocabularyPath);
                _logger.LogInformation("Loaded ONNX spell model {Model} ({Count} vocabulary entries).", modelPath, _vocabulary.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load the ONNX model; using the dictionary checker.");
                _session?.Dispose();
                _session = null;
            }
        }

        public bool IsModelLoaded => _session is not null && _vocabulary.Count > 0;

        public SpellResult CheckWord(string word)
        {
            if (!IsModelLoaded)
                return _fallback.CheckWord(word);

            var normalized = ArabicNormalizer.Normalize(ArabicNormalizer.TrimPunctuation(word));
            if (normalized.Length == 0 || !ArabicNormalizer.IsArabicWord(normalized))
                return SpellResult.Correct(word ?? string.Empty);

            try
            {
                var errorProbability = Predict(normalized);
                if (errorProbability < ErrorThreshold)
                    return SpellResult.Correct(normalized, 1 - errorProbability);

                // The model tells us *that* the word is wrong; the dictionary tells us what to
                // replace it with.
                var suggestions = _fallback.GetSuggestions(normalized);
                return SpellResult.Error(normalized, suggestions, errorProbability);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ONNX inference failed for '{Word}'; falling back.", normalized);
                return _fallback.CheckWord(word);
            }
        }

        public List<string> GetSuggestions(string word) => CheckWord(word).Suggestions;

        public void Dispose() => _session?.Dispose();

        private double Predict(string normalized)
        {
            var ids = new long[normalized.Length];
            for (var i = 0; i < normalized.Length; i++)
                ids[i] = _vocabulary.TryGetValue(normalized[i], out var id) ? id : 0;

            var tensor = new DenseTensor<long>(ids, new[] { 1, ids.Length });
            var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };

            using var results = _session!.Run(inputs);
            var logits = results.First().AsEnumerable<float>().ToArray();
            if (logits.Length < 2)
                throw new InvalidOperationException($"Expected 2 output logits, got {logits.Length}.");

            return Softmax(logits[0], logits[1]);
        }

        private static double Softmax(float correctLogit, float errorLogit)
        {
            var max = Math.Max(correctLogit, errorLogit);
            var correct = Math.Exp(correctLogit - max);
            var error = Math.Exp(errorLogit - max);
            return error / (correct + error);
        }

        private void LoadVocabulary(string path)
        {
            long index = 0;
            foreach (var line in File.ReadLines(path))
            {
                if (line.Length > 0)
                    _vocabulary[line[0]] = index;
                index++;
            }
        }
    }
}
