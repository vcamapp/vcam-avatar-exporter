using System;
using System.Collections.Generic;
using System.Linq;

namespace VCam.AvatarExporter
{
    /// <summary>
    /// Text for the user, kept as a key and its arguments until shown, so a report written before the user switches
    /// the editor language shows in the new language.
    /// </summary>
    internal sealed class Message : IEquatable<Message>
    {
        internal string Key { get; }

        /// <summary>Values put into the text. A <see cref="Term"/> is translated as well.</summary>
        internal IReadOnlyList<object> Args { get; }

        internal Message(string key, params object[] args)
        {
            Key = key;
            Args = args;
        }

        public override string ToString() =>
            Strings.Format(Key, Args.Select(arg => arg is Term term ? Strings.Tr(term.Key) : arg).ToArray());

        public bool Equals(Message other) => other != null && Key == other.Key && Args.SequenceEqual(other.Args);

        public override bool Equals(object obj) => Equals(obj as Message);

        public override int GetHashCode() => Key.GetHashCode();
    }

    /// <summary>An argument of a <see cref="Message"/> that is itself translated, such as the name of a feature.</summary>
    internal readonly struct Term : IEquatable<Term>
    {
        internal string Key { get; }

        internal Term(string key) => Key = key;

        public bool Equals(Term other) => Key == other.Key;

        public override bool Equals(object obj) => obj is Term other && Equals(other);

        public override int GetHashCode() => Key.GetHashCode();
    }
}
