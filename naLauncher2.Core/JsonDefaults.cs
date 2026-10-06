using System.Text.Json;
using System.Text.Json.Serialization;

namespace naLauncher2.Core
{
    public static class JsonDefaults
    {
        /// <summary>
        /// Shared by the library and settings files - use it for every read/write of either, or round-tripping breaks.
        /// </summary>
        public static readonly JsonSerializerOptions Options = new()
        {
            PropertyNameCaseInsensitive = true,
            IgnoreReadOnlyFields = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
            IgnoreReadOnlyProperties = true,
        };
    }
}
