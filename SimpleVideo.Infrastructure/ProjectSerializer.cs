using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using SimpleVideo.Core.Models;

namespace SimpleVideo.Infrastructure;

public class ProjectSerializer
{
    private class SvpFileData
    {
        [JsonPropertyName("version")]
        public int Version { get; set; } = 1;

        [JsonPropertyName("project")]
        public Project Project { get; set; } = new();
    }

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
    };

    public static void SaveProject(Project project, string filePath)
    {
        if (project == null) throw new ArgumentNullException(nameof(project));
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));

        var data = new SvpFileData
        {
            Version = 1,
            Project = project
        };

        var json = JsonSerializer.Serialize(data, Options);
        File.WriteAllText(filePath, json);
    }

    public static Project LoadProject(string filePath)
    {
        if (string.IsNullOrEmpty(filePath)) throw new ArgumentException("File path cannot be null or empty.", nameof(filePath));
        if (!File.Exists(filePath)) throw new FileNotFoundException("Project file not found.", filePath);

        var json = File.ReadAllText(filePath);
        var data = JsonSerializer.Deserialize<SvpFileData>(json, Options);

        if (data == null || data.Project == null)
        {
            throw new InvalidDataException("Invalid .svp project file structure.");
        }

        return data.Project;
    }
}
