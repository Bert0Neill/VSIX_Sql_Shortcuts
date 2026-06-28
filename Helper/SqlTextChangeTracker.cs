using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Diagnostics;

namespace VSIX_SqlShortcuts.Helper
{
    public class SqlTextChangeTracker
    {
        private readonly IWpfTextView _view;

        public SqlTextChangeTracker(IWpfTextView view)
        {
            _view = view ?? throw new ArgumentNullException(nameof(view));
            _view.TextBuffer.Changed += OnTextChanged;
        }

        private void OnTextChanged(object sender, TextContentChangedEventArgs e)
        {
            try
            {
                string fullText = _view.TextSnapshot.GetText();

                Debug.WriteLine("========== SQL EDITOR ==========");
                Debug.WriteLine(fullText);

                // Optional: detect last typed word
                string lastWord = GetLastWord(fullText);
                Debug.WriteLine($"LAST WORD: {lastWord}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
            }
        }

        private string GetLastWord(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            int i = text.Length - 1;

            while (i >= 0 && !char.IsWhiteSpace(text[i]))
                i--;

            return text.Substring(i + 1);
        }

        public void Dispose()
        {
            _view.TextBuffer.Changed -= OnTextChanged;
        }
    }
}