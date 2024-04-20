# CS2-AutoRestart
 Plugin for automatic server restart for daily restart of Counter-Strike 2 (CS2). 
 > [!IMPORTANT]  
 > It is required for the server to have hibernation disabled: `sv_hibernate_when_empty` set to `false`.

# Features
 - [x] Automatically checks the current time of the Counter-Strike 2 server, restarts it according to the schedule every day.
 - [x] Notifies players about the upcoming server restart.
 - [x] Translations.
 
# Installation

 ### Requirements

  - [Metamod:Source](https://www.sourcemm.net/downloads.php/?branch=master) (Dev Build)
  - [CounterStrikeSharp](https://github.com/roflmuffin/CounterStrikeSharp) (Version `178` or higher)

  Download the latest release of CS2-AutoRestart from the [GitHub Release Page](https://github.com/Levchik97/CS2-AutoRestart/releases).

  Extract the contents of the archive into your `counterstrikesharp` folder.

 ### Build Instructions

  If you want to build CS2-AutoRestart from the source, follow these instructions:

  ```bash
  git clone https://github.com/Levchik97/CS2-AutoRestart && cd CS2-AutoRestart

  # Make sure the CounterStrikeSharp dependacy has a valid path.
  dotnet build --configuration Release
  ```

# Confiuration
 ```json
{
  "ConfigVersion": 1,
  "RestartTime": "3:50",
  "NotifyPlayersBeforeRestart": true,
  "MinPlayersInstantShutdown": 1,
  "MinPlayerPercentageShutdownAllowed": 0.6,
  "ShutdownOnMapChangeIfPendingUpdate": true
}
 ```
