using System;
using System.Text.RegularExpressions;

namespace DMI.Xenoarch.Core
{
    /// <summary>Anything persistent that is addressed by a stable string id (definitions, world objects, instances).</summary>
    public interface IHasStableId
    {
        string Id { get; }
    }

    /// <summary>
    /// Stable identifier rules.
    /// Definition ids are authored, human-readable and never change once shipped (e.g. "item.tool.brush").
    /// Instance ids (artifacts, recovered items, world objects) are GUIDs generated once and saved.
    /// </summary>
    public static class StableId
    {
        static readonly Regex DefinitionPattern = new Regex("^[a-z0-9]+([._-][a-z0-9]+)*$", RegexOptions.Compiled);

        public static string NewInstanceId() => Guid.NewGuid().ToString("N");

        public static bool IsValidDefinitionId(string id) => !string.IsNullOrWhiteSpace(id) && DefinitionPattern.IsMatch(id);

        /// <summary>Lower-case slug for generating a default id from an asset name.</summary>
        public static string Slug(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return "unnamed";
            var s = Regex.Replace(text.Trim().ToLowerInvariant(), "[^a-z0-9]+", "_");
            return s.Trim('_');
        }
    }
}
