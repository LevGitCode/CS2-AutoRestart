namespace AutoRestart
{
    using CounterStrikeSharp.API;
    using CounterStrikeSharp.API.Core;
    using CounterStrikeSharp.API.Core.Attributes;
    using CounterStrikeSharp.API.Modules.Timers;
    using CounterStrikeSharp.API.Modules.Cvars;
    using CounterStrikeSharp.API.Modules.Utils;
    using Microsoft.Extensions.Logging;

    [MinimumApiVersion(178)]
    public class AutoRestart : BasePlugin, IPluginConfig<PluginConfig>
    {
        public override string ModuleName => "AutoRestart";
        public override string ModuleAuthor => "Lev";
        public override string ModuleDescription => "Auto Restart for Counter-Strike 2.";
        public override string ModuleVersion => "1.0.0";

        public required PluginConfig Config { get; set; } = new();
        private static readonly Dictionary<int, bool> PlayersNotified = new();
        private static ConVar? _svVisibleMaxPlayers;
        private static bool _isServerLoading;
        private static bool _restartRequired;

        private double _timeUntilRestart = -1;  // Инициализация переменной для хранения времени до перезагрузки

        private Timer? _currentTimer;

        public override void Load(bool hotReload)
        {
            _svVisibleMaxPlayers = ConVar.Find("sv_visiblemaxplayers");

            RegisterEventHandler<EventPlayerSpawn>(OnPlayerSpawn);

            RegisterListener<Listeners.OnServerHibernationUpdate>(OnServerHibernationUpdate);
            RegisterListener<Listeners.OnClientConnected>(OnClientConnected);
            RegisterListener<Listeners.OnClientDisconnect>(OnClientDisconnect);
            RegisterListener<Listeners.OnMapStart>(OnMapStart);
            RegisterListener<Listeners.OnMapEnd>(OnMapEnd);

            base.Load(hotReload);

            SetupRestartTimer();
        }

        public override void Unload(bool hotReload)
        {
            if (hotReload) // Если это горячая перезагрузка, сначала отменяем текущий таймер
            {
                CancelCurrentTimer();
            }
            else // Если это обычная выгрузка, просто отменяем текущий таймер
            {
                CancelCurrentTimer();
            }

            base.Unload(hotReload);
        }

        public void OnConfigParsed(PluginConfig newConfig)
        {
            if (newConfig.Version < Config.Version)
            {
                Logger.LogWarning(Localizer["AutoRestart.Console.ConfigVersionMismatch", Config.Version, newConfig.Version]);
            }

            if (newConfig.RestartTime != Config.RestartTime)
            {
                Logger.LogInformation(Localizer["AutoRestart.Console.ConfigUpdate", Config.RestartTime, newConfig.RestartTime]);
                Config = newConfig;
                SetupRestartTimer();
            }
            else
            {
                Logger.LogInformation(Localizer["AutoRestart.Console.NoChangeInRestartTime"]);
                Config = newConfig;
            }
        }

        private void OnServerHibernationUpdate(bool isHibernating)
        {
            if (isHibernating) Logger.LogInformation(Localizer["AutoRestart.Console.HibernateWarning"]);
        }

        private static void OnMapStart(string mapName)
        {
            PlayersNotified.Clear();
            _isServerLoading = false;
        }

        private void OnMapEnd()
        {
            if (_restartRequired && Config.ShutdownOnMapChangeIfPendingUpdate) ShutdownServer();
            _isServerLoading = true;
        }

        private static void OnClientConnected(int playerSlot)
        {
            CCSPlayerController player = Utilities.GetPlayerFromSlot(playerSlot);
            if (!player.IsValid || player.IsBot || player.IsHLTV) return;

            PlayersNotified.Add(playerSlot, false);
        }

        private static void OnClientDisconnect(int playerSlot)
        {
            PlayersNotified.Remove(playerSlot);
        }

        private double CalculateTimeUntilRestart()
        {
            string restartTimeString = Config.RestartTime;

            if (!TimeSpan.TryParse(restartTimeString, out TimeSpan restartTime))
            {
                Logger.LogError($"Invalid restart time format in configuration: {restartTimeString}");
                return -1; // Возвращаем -1, чтобы указать на ошибку в расчёте
            }

            DateTime currentTime = DateTime.Now;
            DateTime dailyRestartTime = new DateTime(currentTime.Year, currentTime.Month, currentTime.Day, restartTime.Hours, restartTime.Minutes, 0);

            if (currentTime >= dailyRestartTime)
            {
                dailyRestartTime = dailyRestartTime.AddDays(1);
            }

            return (dailyRestartTime - currentTime).TotalSeconds; // Возвращаем время до перезагрузки в секундах
        }

        private void SetupRestartTimer()
        {
            _timeUntilRestart = CalculateTimeUntilRestart();
            if (_timeUntilRestart < 0)
            {
                Logger.LogError(Localizer["AutoRestart.Error.FailedToCalculateRestart"]);
                return;
            }

            // Отменяем текущий таймер, если он уже был установлен
            CancelCurrentTimer();

            // Устанавливаем новый таймер, который вызовет ManageServerRestart
            _currentTimer = new Timer((float)_timeUntilRestart, () => {
                Server.NextFrame(ManageServerRestart);
            });

            DateTime nextRestartDateTime = DateTime.Now.AddSeconds(_timeUntilRestart);
            Logger.LogInformation(Localizer["AutoRestart.Console.RestartScheduled", nextRestartDateTime]);
        }

        private void CancelCurrentTimer()
        {
            if (_currentTimer == null) return;

            _currentTimer.Kill();
            _currentTimer = null;
        }

        private bool CheckPlayers(int players)
        {
            var slots = _svVisibleMaxPlayers?.GetPrimitiveValue<int>() ?? -1;

            if (slots == -1) slots = Server.MaxPlayers;

            return (float)players / slots < Config.MinPlayerPercentageShutdownAllowed ||
                   Config.MinPlayersInstantShutdown >= players;
        }

        private HookResult OnPlayerSpawn(EventPlayerSpawn @event, GameEventInfo info)
        {
            CCSPlayerController player = @event.Userid;

            // Проверяем, является ли игрок допустимым для уведомления
            if (!player.IsValid || player.IsBot || player.TeamNum <= (byte)CsTeam.Spectator) return HookResult.Continue;

            // Проверяем, был ли игрок уже уведомлен о перезапуске
            if (PlayersNotified.TryGetValue(player.Slot, out bool notified) && notified) return HookResult.Continue;

            // Отмечаем игрока как уведомленного
            PlayersNotified[player.Slot] = true;

            // Отправляем уведомление игроку о предстоящем перезапуске
            NotifyPlayerAboutRestart(player);

            return HookResult.Continue;
        }

        private void NotifyPlayerAboutRestart(CCSPlayerController player)
        {
            double secondsUntilRestart = CalculateTimeUntilRestart();
            if (secondsUntilRestart < 0)
            {
                player.PrintToChat(Localizer["AutoRestart.Error.CannotCalculateRestartTime"]);
                return; // Обработка ошибки
            }

            // Расчёт точного времени перезагрузки
            DateTime restartTime = DateTime.Now.AddSeconds(secondsUntilRestart);
            string formattedRestartTime = restartTime.ToString("yyyy-MM-dd HH:mm:ss");  // Форматируем дату и время

            double minutesUntilRestart = secondsUntilRestart / 60;
            bool useMinutes = minutesUntilRestart >= 1;
            double displayTime = useMinutes ? minutesUntilRestart : secondsUntilRestart;
            string timeUnitLabel = useMinutes ? "AutoRestart.Chat.MinuteLabel" : "AutoRestart.Chat.SecondLabel";
            string pluralSuffix = displayTime > 1 ? "s" : "";
            string timeToRestart = $"{Math.Floor(displayTime)} {Localizer[timeUnitLabel]}{pluralSuffix}";

            // Включаем в сообщение точное время перезагрузки
            string message = $"{Localizer["AutoRestart.Chat.Prefix"]} {Localizer["AutoRestart.Chat.RestartNotification", timeToRestart]}. Server will restart at {formattedRestartTime}.";
            player.PrintToChat(message);
        }


        private void ManageServerRestart()
        {
            Logger.LogInformation($" DEBUG Timer triggered at {DateTime.Now.ToString("HH:mm:ss")}. Checking conditions for restart.");

            // Получаем текущих игроков на сервере
            List<CCSPlayerController> players = GetCurrentPlayers();

            // Проверяем, подходит ли момент для перезагрузки
            if (_isServerLoading || !CheckPlayers(players.Count)) return;

            // Уведомляем всех игроков о предстоящем перезапуске
            foreach (var player in players)
            {
                NotifyPlayerAboutRestart(player);
                PlayersNotified[player.Slot] = true;
            }

            // Устанавливаем таймер для перезагрузки сервера
            if (_timeUntilRestart >= 0)
            {
                AddTimer((float)_timeUntilRestart, PrepareServerShutdown);
                _restartRequired = true;
            }
            else
            {
                Logger.LogWarning("Time until restart is not set, recalculating...");
                _timeUntilRestart = CalculateTimeUntilRestart();

                if (!(_timeUntilRestart >= 0)) return;

                AddTimer((float)_timeUntilRestart, PrepareServerShutdown);
                _restartRequired = true;
            }
        }

        private void PrepareServerShutdown()
        {
            List<CCSPlayerController> players = GetCurrentPlayers();

            foreach (var player in players)
            {
                switch (player.Connected)
                {
                    case PlayerConnectedState.PlayerConnected:
                    case PlayerConnectedState.PlayerConnecting:
                    case PlayerConnectedState.PlayerReconnecting:
                        Server.ExecuteCommand($"kickid {player.UserId} Due to server restart, the server is now restarting.");
                        break;
                }
            }

            AddTimer(1, ShutdownServer);
        }

        private void ShutdownServer()
        {
            Logger.LogInformation(Localizer["AutoRestart.Console.ServerShutdownInitiated"]);
            Server.ExecuteCommand("quit");
        }

        private static List<CCSPlayerController> GetCurrentPlayers()
        {
            return Utilities.GetPlayers().Where(
                controller => controller is { IsValid: true, IsBot: false, IsHLTV: false }).ToList();
        }
    }
}
