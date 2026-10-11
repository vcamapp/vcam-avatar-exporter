using System.Collections.Generic;

namespace VCam.AvatarExporter
{
    /// <summary>What an export changed or could not carry over, shown to the user after exporting.</summary>
    internal sealed class ExportReport
    {
        private readonly List<Message> _notes = new();

        internal IReadOnlyList<Message> Notes => _notes;

        internal void Add(string key, params object[] args) => _notes.Add(new Message(key, args));
    }
}
