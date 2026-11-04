using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace LogicSim.Simulation
{
    /// <summary>Saves and loads circuit documents in a directory.</summary>
    public sealed class CircuitDocumentStore
    {
        public string Directory { get; }

        public CircuitDocumentStore(string directory)
        {
            Directory = directory;
        }

        public string PathFor(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new CircuitException("Name must not be empty");
            var file = SanitizeFileName(name);
            if (!file.EndsWith(CircuitSerializer.FileExtension, StringComparison.OrdinalIgnoreCase))
                file += CircuitSerializer.FileExtension;
            return Path.Combine(Directory, file);
        }

        public IReadOnlyList<string> List()
        {
            if (!System.IO.Directory.Exists(Directory))
                return Array.Empty<string>();
            return System.IO.Directory.GetFiles(Directory, "*" + CircuitSerializer.FileExtension)
                .Select(p => Path.GetFileName(p).Substring(0, Path.GetFileName(p).Length - CircuitSerializer.FileExtension.Length))
                .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        public string Save(Circuit circuit, string name)
        {
            System.IO.Directory.CreateDirectory(Directory);
            var path = PathFor(name);
            File.WriteAllText(path, CircuitSerializer.Serialize(circuit, name));
            return path;
        }

        public string Read(string name)
        {
            var path = PathFor(name);
            if (!File.Exists(path))
                throw new CircuitException($"No saved circuit named '{name}'");
            return File.ReadAllText(path);
        }

        public static string SanitizeFileName(string name)
        {
            var invalid = Path.GetInvalidFileNameChars();
            var chars = name.Trim().Select(c => invalid.Contains(c) || c == '/' || c == '\\' ? '_' : c).ToArray();
            var result = new string(chars).Trim('.', ' ');
            return result.Length == 0 ? "circuit" : result;
        }
    }
}
