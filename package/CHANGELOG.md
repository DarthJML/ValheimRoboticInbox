# Changelog

## 1.3.0
- Ground pickup: the inbox collects loose items lying on the ground near it (20 m by default) and sorts them.
  Items a player throws down are left alone.
  Made for wolf pens and mob farms. It never harvests crops or plants and never takes placed items.
- New `GroundPickup` settings: `Enabled`, `Radius`, `SweepSeconds`, `OnlyTheseItems`, `NeverTheseItems`.
- Server and all players must update to 1.3.0 together.

## 1.2.0
- Multiplayer version check: the server disconnects players who don't have Robotic Inbox or run a different
  version (`Server.RequireMod`, default on). Players with the mod see both versions in the error message.
- Joining a server without the mod is safe and logged.
- New icon, plain-English README, this changelog.

## 1.1.0
- The inbox is now a Viking Cybercrate: timber-and-stone crate with glowing cyan runes and its own build icon.
- New `Visual.GlowIntensity` setting.

## 1.0.0
- First release: Robotic Inbox for Valheim (10 Wood + 10 Stone), auto-sorts into nearby matching chests.
- Server pushes `ScanRadius` and inbox size to players.
