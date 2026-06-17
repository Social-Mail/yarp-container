using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;

namespace DotNetReverseProxy;

public class JsonLogger {

    public static JsonLogger Instance = new JsonLogger();

    private JsonSerializerOptions options;
    private TextWriter error;
    private TextWriter console;

    public JsonLogger()
    {
        options = new JsonSerializerOptions
        {
            IncludeFields = true,
            IndentCharacter = '\t',
            IndentSize = 1,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull
        };
        this.error = Console.Error;
        this.console = Console.Out;
    }

    public void Log<T>(T item) {
        console.WriteLine(System.Text.Json.JsonSerializer.Serialize<T>(item, options));
    }

    public void LogError<T>(T item) {
        error.WriteLine(System.Text.Json.JsonSerializer.Serialize<T>(item, options));
    }

}