namespace ScreenSpell.Core.Interfaces
{
    /// <summary>The two verdicts the user can pass on a word without leaving the overlay.</summary>
    public interface IUserWordList
    {
        void AddToDictionary(string word);

        void IgnoreWord(string word);
    }
}
