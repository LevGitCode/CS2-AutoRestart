namespace AutoRestart
{
    using System;
    using System.Text.Json.Serialization;
    using CounterStrikeSharp.API.Core;

    public sealed class PluginConfig : BasePluginConfig
    {
        [JsonPropertyName("ConfigVersion")]
        public override int Version { get; set; } = 1;

        [JsonPropertyName("RestartTime")]
        public string RestartTime { get; set; } = "03:50";  // Время перезагрузки сервера в формате "HH:mm"

        [JsonPropertyName("NotifyPlayersBeforeRestart")]
        public bool NotifyPlayersBeforeRestart { get; set; } = true;  //параметр для управления уведомлениями

        [JsonPropertyName("MinPlayersInstantShutdown")]
        public int MinPlayersInstantShutdown { get; set; } = 1;

        [JsonPropertyName("MinPlayerPercentageShutdownAllowed")]
        public float MinPlayerPercentageShutdownAllowed { get; set; } = 0.6f;

        [JsonPropertyName("ShutdownOnMapChangeIfPendingUpdate")]
        public bool ShutdownOnMapChangeIfPendingUpdate { get; set; } = true;
    }
}
