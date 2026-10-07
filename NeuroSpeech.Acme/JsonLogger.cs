using System;
using System.IO;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace NeuroSpeech;


public class DebugLogger
{
    private readonly JsonSerializerOptions options;

    public DebugLogger(JsonSerializerOptions options)
    {
        this.options = options;
    }

    public void Log<T>(T item)
    {
        Console.WriteLine(JsonSerializer.Serialize(item, options));
    }
}

public class JsonLogger {

    private JsonSerializerOptions options;
    private TextWriter error;
    private TextWriter console;

    public readonly DebugLogger? DebugLogger;

    public JsonLogger()
    {
        options = new JsonSerializerOptions
        {
            IncludeFields = true,
            IndentCharacter = '\t',
            IndentSize = 1,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        };
        this.error = Console.Error;
        this.console = Console.Out;
        if( Environment.GetEnvironmentVariable("LOG_MODE")
             ?.Equals("debug", StringComparison.OrdinalIgnoreCase) ?? false)
        {
            this.DebugLogger = new DebugLogger(options);
        }
    }

    public void Log<T>(T item) {
        console.WriteLine(System.Text.Json.JsonSerializer.Serialize<T>(item, options));
    }

    public void Log(Exception item)
    {
        console.WriteLine(System.Text.Json.JsonSerializer.Serialize(new
        {
            error = item.Message,
            details = item.ToString()
        }, options));
    }

    public void LogError<T>(T item) {
        error.WriteLine(System.Text.Json.JsonSerializer.Serialize<T>(item, options));
    }

    public void LogError(Exception item)
    {
        error.WriteLine(System.Text.Json.JsonSerializer.Serialize(new { 
            error = item.Message,
            details = item.ToString()
        }, options));
    }

}