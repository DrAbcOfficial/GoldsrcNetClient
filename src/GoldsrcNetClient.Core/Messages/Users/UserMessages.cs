namespace GoldsrcNetClient.Core.Messages.Users;

/// <summary>Data for the CurWeapon user message.</summary>
/// <param name="IsActive">1 if the weapon is currently active, 0 otherwise.</param>
/// <param name="WeaponId">Weapon identifier (mod-specific).</param>
/// <param name="ClipAmmo">Ammunition remaining in the current clip/magazine.</param>
public sealed record CurWeaponMessage(byte IsActive, byte WeaponId, byte ClipAmmo) : IServerMessage;

/// <summary>Data for the Damage user message (damage indicator).</summary>
/// <param name="DamageSave">Damage absorbed by armor.</param>
/// <param name="DamageTake">Damage taken to health.</param>
/// <param name="DamageType">Bitwise damage type flags.</param>
/// <param name="OriginX">X coordinate of the damage origin.</param>
/// <param name="OriginY">Y coordinate of the damage origin.</param>
/// <param name="OriginZ">Z coordinate of the damage origin.</param>
public sealed record DamageMessage(byte DamageSave, byte DamageTake, int DamageType, float OriginX, float OriginY, float OriginZ) : IServerMessage;

/// <summary>Data for the DeathMsg user message (Half-Life Deathmatch format).</summary>
/// <param name="KillerId">Player index of the killer.</param>
/// <param name="VictimId">Player index of the victim.</param>
/// <param name="IsHeadshot">1 if a headshot, 0 otherwise. Only present in CS.</param>
/// <param name="WeaponName">Truncated weapon name (no "weapon_" prefix in CS).</param>
public sealed record DeathMsgMessage(byte KillerId, byte VictimId, byte IsHeadshot, string WeaponName) : IServerMessage;

/// <summary>Data for the Health user message.</summary>
/// <param name="Health">Current health value.</param>
public sealed record HealthMessage(byte Health) : IServerMessage;

/// <summary>Data for the Battery (armor) user message.</summary>
/// <param name="Armor">Current armor value.</param>
public sealed record BatteryMessage(short Armor) : IServerMessage;

/// <summary>Data for the AmmoX user message (reserve ammo update).</summary>
/// <param name="AmmoId">Ammo type identifier.</param>
/// <param name="Amount">Amount of ammo available.</param>
public sealed record AmmoXMessage(byte AmmoId, byte Amount) : IServerMessage;

/// <summary>Data for the AmmoPickup user message.</summary>
/// <param name="AmmoId">Ammo type identifier.</param>
/// <param name="Amount">Amount picked up.</param>
public sealed record AmmoPickupMessage(byte AmmoId, byte Amount) : IServerMessage;

/// <summary>Data for the FlashBat user message (flashlight battery).</summary>
/// <param name="ChargePercentage">Battery charge percentage (0-100).</param>
public sealed record FlashBatMessage(byte ChargePercentage) : IServerMessage;

/// <summary>Data for the Flashlight user message.</summary>
/// <param name="IsOn">1 if the flashlight is active, 0 otherwise.</param>
/// <param name="ChargePercent">Battery charge percentage.</param>
public sealed record FlashlightMessage(byte IsOn, byte ChargePercent) : IServerMessage;

/// <summary>Data for the GameMode user message.</summary>
/// <param name="GameMode">Current game mode identifier (0 = undecided/spectator, 1 = singleplayer, etc.).</param>
public sealed record GameModeMessage(byte GameMode) : IServerMessage;

/// <summary>Data for the Geiger user message (radiation indicator).</summary>
/// <param name="Distance">Distance to the hazard.</param>
public sealed record GeigerMessage(byte Distance) : IServerMessage;

/// <summary>Data for the HideWeapon user message.</summary>
/// <param name="Flags">Bitmask of HUD elements to hide.</param>
public sealed record HideWeaponMessage(byte Flags) : IServerMessage;

/// <summary>Data for the HudText user message.</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
public sealed record HudTextMessage(string TextCode, byte Style) : IServerMessage;

/// <summary>Data for the ItemPickup user message.</summary>
/// <param name="ItemName">Name of the picked-up item.</param>
public sealed record ItemPickupMessage(string ItemName) : IServerMessage;

/// <summary>Data for the ScreenFade user message (screen color fading).</summary>
/// <param name="Duration">Fade-in duration in milliseconds.</param>
/// <param name="HoldTime">Duration to hold the fade.</param>
/// <param name="FadeFlags">Fade type flags (1 = fade in, 2 = fade out, 4 = modulate).</param>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
/// <param name="A">Alpha component (0-255).</param>
public sealed record ScreenFadeMessage(short Duration, short HoldTime, short FadeFlags, byte R, byte G, byte B, byte A) : IServerMessage;

/// <summary>Data for the ScreenShake user message.</summary>
/// <param name="Amplitude">Shake amplitude.</param>
/// <param name="Duration">Shake duration in milliseconds.</param>
/// <param name="Frequency">Shake frequency.</param>
public sealed record ScreenShakeMessage(short Amplitude, short Duration, short Frequency) : IServerMessage;

/// <summary>Data for the SetFOV user message.</summary>
/// <param name="Fov">Field of view value (default 90).</param>
public sealed record SetFovMessage(byte Fov) : IServerMessage;

/// <summary>Data for the StatusIcon user message.</summary>
/// <param name="Status">1 to show the icon, 0 to hide.</param>
/// <param name="IconName">Icon sprite name.</param>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
public sealed record StatusIconMessage(byte Status, string IconName, byte R, byte G, byte B) : IServerMessage;

/// <summary>Data for the TeamInfo user message.</summary>
/// <param name="PlayerIndex">Player index (1-based).</param>
/// <param name="TeamName">Team name string.</param>
public sealed record TeamInfoMessage(byte PlayerIndex, string TeamName) : IServerMessage;

/// <summary>Data for the TextMsg user message.</summary>
/// <param name="MsgDest">Message destination (1 = console, 2 = center, 3 = chat, 4 = center no stay, 5 = HUD_PRINTCENTER).</param>
/// <param name="Message">The text message content.</param>
public sealed record TextMsgMessage(byte MsgDest, string Message) : IServerMessage;

/// <summary>Data for the WeaponList user message (weapon registration).</summary>
/// <param name="WeaponName">Weapon classname.</param>
/// <param name="Ammo1Id">Primary ammo type ID.</param>
/// <param name="Ammo1Max">Maximum primary ammo.</param>
/// <param name="Ammo2Id">Secondary ammo type ID.</param>
/// <param name="Ammo2Max">Maximum secondary ammo.</param>
/// <param name="Slot">Weapon slot number.</param>
/// <param name="Position">Position within the slot.</param>
/// <param name="WeaponId">Weapon ID.</param>
/// <param name="Flags">Weapon flags.</param>
public sealed record WeaponListMessage(string WeaponName, byte Ammo1Id, byte Ammo1Max, byte Ammo2Id, byte Ammo2Max, byte Slot, byte Position, byte WeaponId, byte Flags) : IServerMessage;

/// <summary>Data for the WeapPickup user message (weapon icon pickup display).</summary>
/// <param name="WeaponName">Name of the weapon that was picked up.</param>
public sealed record WeapPickupMessage(string WeaponName) : IServerMessage;

/// <summary>Data for the SayText user message (chat message).</summary>
/// <param name="SenderId">Player index of the sender (0 for server).</param>
/// <param name="Message">The chat message text.</param>
public sealed record SayTextMessage(byte SenderId, string Message) : IServerMessage;

/// <summary>Data for the Train user message.</summary>
/// <param name="Position">Train control position (0 = inactive, 1 = active).</param>
public sealed record TrainMessage(byte Position) : IServerMessage;

/// <summary>Data for the VGUIMenu user message.</summary>
/// <param name="MenuType">Menu type identifier.</param>
/// <param name="Data">Raw remaining data as a null-terminated string block.</param>
public sealed record VguiMenuMessage(byte MenuType, string Data) : IServerMessage;

/// <summary>Data for the ResetHUD user message.</summary>
public sealed record ResetHudMessage() : IServerMessage;

/// <summary>Data for the InitHUD user message.</summary>
public sealed record InitHudMessage() : IServerMessage;

/// <summary>Data for the GameTitle user message.</summary>
/// <param name="Show">1 to show the game title, 0 to hide.</param>
public sealed record GameTitleMessage(byte Show) : IServerMessage;

// ─── Counter-Strike specific ───

/// <summary>Data for the Money user message (Counter-Strike).</summary>
/// <param name="Amount">Current money amount.</param>
/// <param name="FlashAmount">Number of times the money display should flash (0 means don't flash).</param>
public sealed record MoneyMessage(short Amount, byte FlashAmount) : IServerMessage;

/// <summary>Data for the Radar user message (Counter-Strike).</summary>
/// <param name="PlayerIndex">Player index.</param>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public sealed record RadarMessage(byte PlayerIndex, float X, float Y, float Z) : IServerMessage;

/// <summary>Data for the ScoreInfo user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="Score">Player score.</param>
/// <param name="Deaths">Player deaths.</param>
/// <param name="IsAlive">1 if player is alive, 0 if dead.</param>
/// <param name="TeamId">Team ID.</param>
public sealed record ScoreInfoMessage(byte PlayerId, short Score, short Deaths, byte IsAlive, byte TeamId) : IServerMessage;

/// <summary>Data for the ScoreAttrib user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="Flags">Attribute flags (1 = dead, 2 = bomb carrier, 4 = VIP, 8 = defuser).</param>
public sealed record ScoreAttribMessage(byte PlayerId, byte Flags) : IServerMessage;

/// <summary>Data for the RoundTime user message (Counter-Strike).</summary>
/// <param name="Seconds">Round time remaining in seconds.</param>
public sealed record RoundTimeMessage(short Seconds) : IServerMessage;

/// <summary>Data for the BombDrop user message (Counter-Strike).</summary>
/// <param name="X">Bomb drop X coordinate.</param>
/// <param name="Y">Bomb drop Y coordinate.</param>
/// <param name="Z">Bomb drop Z coordinate.</param>
/// <param name="Planted">1 if the bomb was planted, 0 if dropped.</param>
public sealed record BombDropMessage(float X, float Y, float Z, byte Planted) : IServerMessage;

/// <summary>Data for the BombPickup user message (Counter-Strike).</summary>
public sealed record BombPickupMessage() : IServerMessage;

/// <summary>Data for the HostageK user message (Counter-Strike).</summary>
/// <param name="HostageId">Hostage entity index.</param>
public sealed record HostageKMessage(byte HostageId) : IServerMessage;

/// <summary>Data for the HostagePos user message (Counter-Strike).</summary>
/// <param name="Flag">Update flag (1 on HUD full update).</param>
/// <param name="HostageId">Hostage entity index.</param>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public sealed record HostagePosMessage(byte Flag, byte HostageId, float X, float Y, float Z) : IServerMessage;

/// <summary>Data for the BarTime user message (Counter-Strike).</summary>
/// <param name="Duration">Progress bar duration in seconds.</param>
public sealed record BarTimeMessage(short Duration) : IServerMessage;

/// <summary>Data for the BarTime2 user message (Counter-Strike).</summary>
/// <param name="Duration">Total duration in seconds.</param>
/// <param name="StartPercent">Starting fill percentage.</param>
public sealed record BarTime2Message(short Duration, short StartPercent) : IServerMessage;

/// <summary>Data for the BlinkAcct user message (Counter-Strike).</summary>
/// <param name="BlinkAmount">Number of times to flash the money display.</param>
public sealed record BlinkAcctMessage(byte BlinkAmount) : IServerMessage;

/// <summary>Data for the ArmorType user message (Counter-Strike).</summary>
/// <param name="HasHelmet">1 to show helmet icon, 0 to hide.</param>
public sealed record ArmorTypeMessage(byte HasHelmet) : IServerMessage;

/// <summary>Data for the Crosshair user message (Counter-Strike).</summary>
/// <param name="Show">1 to show the spectator crosshair, 0 to hide.</param>
public sealed record CrosshairMessage(byte Show) : IServerMessage;

/// <summary>Data for the Fog user message.</summary>
/// <param name="R">Red component.</param>
/// <param name="G">Green component.</param>
/// <param name="B">Blue component.</param>
/// <param name="Density">Fog density.</param>
public sealed record FogMessage(byte R, byte G, byte B, byte Density) : IServerMessage;

/// <summary>Data for the NVGToggle user message (Counter-Strike).</summary>
/// <param name="Mode">1 to enable night vision, 0 to disable.</param>
public sealed record NvgToggleMessage(byte Mode) : IServerMessage;

/// <summary>Data for the ReceiveW user message (Counter-Strike).</summary>
/// <param name="ItemId">Received weapon/item ID.</param>
public sealed record ReceiveWMessage(byte ItemId) : IServerMessage;

/// <summary>Data for the ReloadSound user message (Counter-Strike).</summary>
/// <param name="PlayerIndex">Player index.</param>
/// <param name="WeaponId">Weapon ID being reloaded.</param>
public sealed record ReloadSoundMessage(byte PlayerIndex, byte WeaponId) : IServerMessage;

/// <summary>Data for the SendAudio user message (Counter-Strike).</summary>
/// <param name="Channel">Audio channel.</param>
/// <param name="SoundName">Sound file name.</param>
public sealed record SendAudioMessage(byte Channel, string SoundName) : IServerMessage;

/// <summary>Data for the ShadowIdx user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="ShadowId">Shadow index.</param>
public sealed record ShadowIdxMessage(byte PlayerId, byte ShadowId) : IServerMessage;

/// <summary>Data for the ShowMenu user message (Counter-Strike).</summary>
/// <param name="ValidSlots">Bitmask of valid menu slots.</param>
/// <param name="DisplayTime">Menu display duration in seconds (0 = permanent).</param>
/// <param name="NeedMore">1 if the menu has more pages.</param>
/// <param name="Text">Menu text content.</param>
public sealed record ShowMenuMessage(short ValidSlots, byte DisplayTime, byte NeedMore, string Text) : IServerMessage;

/// <summary>Data for the ShowTimer user message (Counter-Strike).</summary>
/// <param name="Show">1 to show the round timer, 0 to hide.</param>
public sealed record ShowTimerMessage(byte Show) : IServerMessage;

/// <summary>Data for the Spectator user message (Counter-Strike).</summary>
/// <param name="PlayerId">Target player index.</param>
/// <param name="Mode">Spectator mode.</param>
public sealed record SpectatorMessage(byte PlayerId, byte Mode) : IServerMessage;

/// <summary>Data for the TeamScore user message (Counter-Strike).</summary>
/// <param name="TeamName">Team name string.</param>
/// <param name="Score">Team score.</param>
public sealed record TeamScoreMessage(string TeamName, short Score) : IServerMessage;

/// <summary>Data for the VoteMenu user message (Counter-Strike).</summary>
/// <param name="ValidSlots">Bitmask of valid vote options.</param>
/// <param name="DisplayTime">Display duration.</param>
/// <param name="Text">Vote text content.</param>
public sealed record VoteMenuMessage(short ValidSlots, byte DisplayTime, string Text) : IServerMessage;

/// <summary>Data for the AllowSpec user message.</summary>
/// <param name="Allowed">1 if spectating is allowed, 0 otherwise.</param>
public sealed record AllowSpecMessage(byte Allowed) : IServerMessage;

/// <summary>Data for the ForceCam user message (Counter-Strike).</summary>
/// <param name="ForceCamValue">Force camera value.</param>
/// <param name="ForceChaseCamValue">Force chase cam value.</param>
/// <param name="Unknown">Unknown third value.</param>
public sealed record ForceCamMessage(byte ForceCamValue, byte ForceChaseCamValue, byte Unknown) : IServerMessage;

/// <summary>Data for the HLTV user message.</summary>
/// <param name="ClientId">Client or player index.</param>
/// <param name="Flags">HLTV flags.</param>
public sealed record HltvMessage(byte ClientId, byte Flags) : IServerMessage;

/// <summary>Data for the BotVoice user message (Counter-Strike: Condition Zero).</summary>
/// <param name="Status">1 if talking, 0 otherwise.</param>
/// <param name="PlayerIndex">Player index.</param>
public sealed record BotVoiceMessage(byte Status, byte PlayerIndex) : IServerMessage;

/// <summary>Data for the BuyClose user message (Counter-Strike).</summary>
public sealed record BuyCloseMessage() : IServerMessage;

/// <summary>Data for the ADStop user message (Counter-Strike).</summary>
public sealed record AdStopMessage() : IServerMessage;

/// <summary>Data for the ItemStatus user message (Counter-Strike).</summary>
/// <param name="ItemBits">Bitmask of carried items.</param>
public sealed record ItemStatusMessage(int ItemBits) : IServerMessage;

/// <summary>Data for the HudTextArgs user message (Counter-Strike).</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
/// <param name="Args">Sub-message strings.</param>
public sealed record HudTextArgsMessage(string TextCode, byte Style, string[] Args) : IServerMessage;

/// <summary>Data for the HudTextPro user message (Counter-Strike).</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
public sealed record HudTextProMessage(string TextCode, byte Style) : IServerMessage;

/// <summary>Data for the HudColor user message (Half-Life: Opposing Force).</summary>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
public sealed record HudColorMessage(byte R, byte G, byte B) : IServerMessage;

/// <summary>Data for the Concuss user message (concussion effect).</summary>
/// <param name="Amount">Concussion intensity.</param>
public sealed record ConcussMessage(byte Amount) : IServerMessage;

// ─── Sven Co-op ───
// All layouts below were verified by reverse-engineering svencoop/cl_dlls/client.dll
// (handler decompiles, see sven_coop_usermsgs.md). Sven replaces several Half-Life
// message formats with wider types; those are re-declared here as Sc* records so the
// wire format stays lossless. Fields whose semantics could not be pinned down from
// the handler code alone keep neutral names (Unknown*/Value*/Float*).

/// <summary>Sven Co-op CurWeapon: wider than the Half-Life format.</summary>
/// <param name="IsActive">1 if the weapon is currently active.</param>
/// <param name="WeaponId">Weapon identifier; -1 hides the weapon HUD.</param>
/// <param name="ClipAmmo">Rounds in the magazine (-1 clamped to 0 by the client).</param>
/// <param name="ReserveAmmo">Reserve ammunition (-1 clamped to 0 by the client).</param>
public sealed record ScCurWeaponMessage(byte IsActive, short WeaponId, int ClipAmmo, int ReserveAmmo) : IServerMessage;

/// <summary>Sven Co-op Health: 32-bit health.</summary>
public sealed record ScHealthMessage(int Health) : IServerMessage;

/// <summary>Sven Co-op Battery (armor): single byte.</summary>
public sealed record ScBatteryMessage(byte Armor) : IServerMessage;

/// <summary>Sven Co-op AmmoX: 32-bit reserve count.</summary>
public sealed record ScAmmoXMessage(byte AmmoId, int Amount) : IServerMessage;

/// <summary>Sven Co-op AmmoPickup: 32-bit count.</summary>
public sealed record ScAmmoPickupMessage(byte AmmoId, int Amount) : IServerMessage;

/// <summary>Sven Co-op WeapPickup: weapon id as a short (Half-Life sends a name string).</summary>
public sealed record ScWeapPickupMessage(short WeaponId) : IServerMessage;

/// <summary>Sven Co-op WeaponList: primary/secondary ammo maxima are 32-bit.</summary>
public sealed record ScWeaponListMessage(
    string WeaponName, byte PrimaryAmmoId, int PrimaryAmmoMax,
    byte SecondaryAmmoId, int SecondaryAmmoMax,
    byte Slot, byte Position, short WeaponId, byte Flags) : IServerMessage;

/// <summary>Sven Co-op TextMsg: destination plus the message and its four format parameters.</summary>
public sealed record ScTextMsgMessage(byte MsgDest, string Message, string Param1, string Param2, string Param3, string Param4) : IServerMessage;

/// <summary>Sven Co-op HudText: a single text/localisation string.</summary>
public sealed record ScHudTextMessage(string Text) : IServerMessage;

/// <summary>Sven Co-op Concuss: direction vector of the concussion effect.</summary>
public sealed record ScConcussMessage(float DirectionX, float DirectionY, float DirectionZ) : IServerMessage;

/// <summary>Sven Co-op Fog (format differs from the Counter-Strike Fog message).</summary>
/// <param name="Leading">Leading short read by the client but not stored.</param>
/// <param name="Enabled">Fog toggle.</param>
/// <param name="OriginX/Y/Z">Coordinates read by the client but not stored.</param>
/// <param name="Unknown">Short read between the coordinates and the colour bytes.</param>
/// <param name="R/G/B">Fog colour.</param>
/// <param name="Value1/Value2">Trailing shorts (fog range/density candidates).</param>
public sealed record ScFogMessage(bool Enabled, float OriginX, float OriginY, float OriginZ,
    short Unknown, byte R, byte G, byte B, short Value1, short Value2) : IServerMessage;

/// <summary>Sven Co-op ShowMenu: byte-sized slot mask, signed display time, flag byte.</summary>
public sealed record ScShowMenuMessage(byte ValidSlots, sbyte DisplayTime, byte Flags, string Text) : IServerMessage;

/// <summary>Sven Co-op HideHUD: 16-bit hide mask (Half-Life's HideWeapon uses a byte).</summary>
public sealed record ScHideHudMessage(short Flags) : IServerMessage;

/// <summary>VoiceMask: per-player voice audibility/ban bitmasks.</summary>
public sealed record VoiceMaskMessage(int AudiblePlayers, int BannedPlayers, byte Flags) : IServerMessage;

/// <summary>Sven Co-op ViewMode: camera perspective.</summary>
public sealed record ScViewModeMessage(bool ThirdPerson) : IServerMessage;

/// <summary>Sven Co-op CdAudio: MP3 track request (0 = stop, 1..30 → media/Half-LifeXX).</summary>
public sealed record ScCdAudioMessage(byte Track) : IServerMessage;

/// <summary>Sven Co-op ClassicMode: classic gameplay mode toggle.</summary>
public sealed record ScClassicModeMessage(bool Enabled) : IServerMessage;

/// <summary>Sven Co-op VModelPos: view model offset.</summary>
public sealed record ScVModelPosMessage(bool Enabled, float X, float Y, float Z) : IServerMessage;

/// <summary>Sven Co-op TimeEnd: round timer end (absolute client time).</summary>
public sealed record ScTimeEndMessage(int Seconds) : IServerMessage;

/// <summary>Sven Co-op OnTank: player is driving a func tank.</summary>
public sealed record ScOnTankMessage(bool OnTank) : IServerMessage;

/// <summary>Sven Co-op Playlist: media playlist switch.</summary>
public sealed record ScPlaylistMessage(string Playlist) : IServerMessage;

/// <summary>Sven Co-op Speaksent: server-requested sentence playback.</summary>
public sealed record ScSentenceMessage(string Sentence) : IServerMessage;

/// <summary>Sven Co-op ValClass: five class/slot values.</summary>
public sealed record ScValClassMessage(short[] Classes) : IServerMessage;

/// <summary>One team entry of the Sven Co-op TeamNames message.</summary>
public sealed record ScTeamNamesTeam(string Name, float ColorR, float ColorG, float ColorB);

/// <summary>Sven Co-op TeamNames: team list with colours.</summary>
public sealed record ScTeamNamesMessage(ScTeamNamesTeam[] Teams) : IServerMessage;

/// <summary>Sven Co-op MOTD chunk.</summary>
public sealed record ScMotdMessage(bool IsFinal, string Text) : IServerMessage;

/// <summary>Sven Co-op ServerName: server hostname (client overrides non-Sven names).</summary>
public sealed record ScServerNameMessage(string ServerName) : IServerMessage;

/// <summary>Sven Co-op ServerVer: server version string (mismatch disconnects the client).</summary>
public sealed record ScServerVersionMessage(string Version) : IServerMessage;

/// <summary>Sven Co-op ServerBuild: server build string.</summary>
public sealed record ScServerBuildMessage(string Build) : IServerMessage;

/// <summary>Sven Co-op NextMap.</summary>
public sealed record ScNextMapMessage(string MapName) : IServerMessage;

/// <summary>Sven Co-op ScoreInfo: float-based scoreboard entry (format differs from CS).</summary>
public sealed record ScScoreInfoMessage(byte PlayerIndex, float Score, int UnknownLong,
    float UnknownFloat1, float UnknownFloat2, byte ClassId, byte UnknownByte1, byte UnknownByte2) : IServerMessage;

/// <summary>Sven Co-op TeamScore: two score values per team.</summary>
public sealed record ScTeamScoreMessage(string TeamName, short Score, short Score2) : IServerMessage;

/// <summary>Sven Co-op Gib: gib burst at a point with a velocity bias.</summary>
public sealed record ScGibMessage(byte Type, float OriginX, float OriginY, float OriginZ,
    float VelocityX, float VelocityY, float VelocityZ) : IServerMessage;

/// <summary>Sven Co-op TE_CUSTOM: subtyped temporary effect.</summary>
public sealed record ScTeCustomMessage(byte SubType, short Id, short Count,
    float OriginX, float OriginY, float OriginZ, byte Value) : IServerMessage;

/// <summary>Sven Co-op CbElec: electrified tripwire state (wire byte: bit0-4 entity, bit6 active).</summary>
public sealed record ScCbElecMessage(bool On, byte Entity) : IServerMessage;

/// <summary>Sven Co-op ShkFlash: shock roach flash at an origin (mode 0 = fire, else impact).</summary>
public sealed record ScShkFlashMessage(float X, float Y, float Z, byte Mode) : IServerMessage;

/// <summary>Sven Co-op TracerDecal: tracer endpoints and decal type.</summary>
public sealed record ScTracerDecalMessage(float StartX, float StartY, float StartZ,
    float EndX, float EndY, float EndZ, byte DecalType, byte Unknown) : IServerMessage;

/// <summary>Sven Co-op SporeTrail: spore trail attach state for an entity.</summary>
public sealed record ScSporeTrailMessage(short Entity, bool Attach) : IServerMessage;

/// <summary>Sven Co-op CreateBlood (a.k.a. SpawnBlood): blood effect.</summary>
public sealed record ScCreateBloodMessage(float X, float Y, float Z, byte Color, byte Amount) : IServerMessage;

/// <summary>Sven Co-op GargSplash: gargantua splash with colour triple (the client
/// reads coordinates and takes their absolute value before use).</summary>
public sealed record ScGargSplashMessage(float X, float Y, float Z, float ColorR, float ColorG, float ColorB) : IServerMessage;

/// <summary>Sven Co-op StartSound: flag-driven sound playback. Optional fields are present
/// only when the corresponding flag bit is set (0x10 entity, 0x1 volume, 0x2 attenuation,
/// 0x4 pitch, 0x8 origin, 0x8000 duration). Channel and sound index are always sent.</summary>
public sealed record ScStartSoundMessage(short Flags, short? Entity, byte? Volume, byte? Attenuation,
    byte? Pitch, float? OriginX, float? OriginY, float? OriginZ, float? Duration, byte Channel, short SoundIndex) : IServerMessage;

/// <summary>Sven Co-op ToxicCloud.</summary>
public sealed record ScToxicCloudMessage(float X, float Y, float Z) : IServerMessage;

/// <summary>Sven Co-op SRDetonate: shock roach detonation.</summary>
public sealed record ScSrDetonateMessage(float X, float Y, float Z, byte Radius) : IServerMessage;

/// <summary>Sven Co-op SRPrimed: shock roach fuse.</summary>
public sealed record ScSrPrimedMessage(byte Entity, float Time) : IServerMessage;

/// <summary>Sven Co-op SRPrimedOff: cancels a primed shock roach.</summary>
public sealed record ScSrPrimedOffMessage(byte Entity) : IServerMessage;

/// <summary>Sven Co-op RampSprite: highly configurable sprite ramp effect. Optional fields
/// are present when the corresponding flag bit is set: 0x1 StartTime, 0x2 FadeIn, 0x4 FadeOut,
/// 0x8 RenderModeFx (low 5 bits mode, high 3 bits fx), 0x10/0x20/0x40 RGB, 0x8000 trailing
/// byte, 0x100 secondary colour (3 coords), 0x200-0x4000 further scale bytes.</summary>
public sealed record ScRampSpriteMessage(short Entity, byte LifeTicks, float X, float Y, float Z, short Flags,
    byte? StartTime, byte? FadeInTime, byte? FadeOutTime, byte? RenderModeFx,
    byte? R, byte? G, byte? B, byte? UnknownBit15,
    float? Color2R, float? Color2G, float? Color2B,
    byte? UnknownBit200, byte? UnknownBit400, byte? UnknownBit800, byte? UnknownBit1000,
    byte? UnknownBit2000, byte? UnknownBit4000, byte? UnknownBit8000) : IServerMessage;

/// <summary>Sven Co-op ShieldRic: shield ricochet spark.</summary>
public sealed record ScShieldRicMessage(float X, float Y, float Z) : IServerMessage;

/// <summary>Sven Co-op WeatherFX: full weather particle description. The trailing short/byte/
/// float groups are weather parameters whose per-field semantics the client passes through
/// opaquely, so they keep neutral names.</summary>
public sealed record ScWeatherFxMessage(short Type,
    float MinsX, float MinsY, float MinsZ, float MaxsX, float MaxsY, float MaxsZ,
    float AngleX, float AngleY, float AngleZ,
    short UnknownShort1, float Float1, byte Byte1, short UnknownShort2, float Float2,
    byte Byte2, byte Byte3, float Float3, byte Byte4, byte Byte5, byte Byte6, byte Byte7,
    float Float4, float Float5, float Float6, float Float7) : IServerMessage;

/// <summary>Sven Co-op CameraMouse: fixed camera mode; mode 2 carries a parameter string.</summary>
public sealed record ScCameraMouseMessage(byte Mode, string Parameter) : IServerMessage;

/// <summary>Sven Co-op Flamethwr: flame thrower flame segment.</summary>
public sealed record ScFlamethrowerMessage(byte Entity, float StartX, float StartY, float StartZ,
    float EndX, float EndY, float EndZ) : IServerMessage;

/// <summary>Sven Co-op ChangeSky: sky box name and colour (all -1 means keep default).</summary>
public sealed record ScChangeSkyMessage(string SkyName, float ColorR, float ColorG, float ColorB) : IServerMessage;

/// <summary>Sven Co-op ToggleElem: HUD element channel on/off.</summary>
public sealed record ScToggleElemMessage(byte Channel, bool State) : IServerMessage;

/// <summary>Sven Co-op CustSpr (a.k.a. CustomSprite): custom HUD sprite element.</summary>
public sealed record ScCustomSpriteMessage(byte Channel, int Flags, string Sprite, byte X, byte Y,
    short Width, short Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    byte Unknown1, byte Unknown2, float Float1, float Float2, float Float3, float Float4, float Float5, byte Unknown3) : IServerMessage;

/// <summary>Sven Co-op NumDisplay: custom numeric HUD element.</summary>
public sealed record ScNumDisplayMessage(byte Channel, int Flags, float Value, byte X, byte Y,
    float Width, float Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    string Font, byte RegionX, byte RegionY, short RegionWidth, short RegionHeight,
    float Float1, float Float2, float Float3, float Float4, byte Unknown) : IServerMessage;

/// <summary>Sven Co-op UpdateNum: value update for a numeric HUD element.</summary>
public sealed record ScUpdateNumMessage(byte Channel, float Value) : IServerMessage;

/// <summary>Sven Co-op TimeDisplay: custom timer HUD element.</summary>
public sealed record ScTimeDisplayMessage(byte Channel, int Flags, float Value1, float Value2,
    float Width, float Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    string Font, byte RegionX, byte RegionY, short RegionWidth, short RegionHeight,
    float Float1, float Float2, float Float3, float Float4, byte Unknown) : IServerMessage;

/// <summary>Sven Co-op UpdateTime: time update for a timer HUD element.</summary>
public sealed record ScUpdateTimeMessage(byte Channel, float Time, float Duration) : IServerMessage;

/// <summary>Sven Co-op InvAdd: inventory item added. The five strings are the item's
/// name/icon/description texts (client stores them opaquely).</summary>
public sealed record ScInventoryAddMessage(int Id, bool Flag1, bool Flag2, bool Flag3, float Time,
    string String1, string String2, string String3, string String4, string String5) : IServerMessage;

/// <summary>Sven Co-op InvRemove: inventory items removed (id 0 = all matching).</summary>
public sealed record ScInventoryRemoveMessage(int Id, byte Flags) : IServerMessage;

/// <summary>Sven Co-op PrtlUpdt (a.k.a. PortalUpdate): portal/monitor entity update with
/// conditional trailing sections per type/style.</summary>
public sealed record ScPortalUpdateMessage(bool Removed, int Entity,
    float[] Vec1, float[] Vec2, byte Type, byte Style, float Life,
    byte Byte1, int Long1, int Long2, bool Flag1,
    int? ModelIndex, float[]? ModelOrigin, float[]? ModelAngles,
    int? Long3, int? Long4, bool? FlagA, bool? FlagB, string? Name) : IServerMessage;

/// <summary>Sven Co-op ClServerInfo: server info handshake (key feeds GenerateKey).</summary>
public sealed record ScClServerInfoMessage(byte Flag, int Value, string Key) : IServerMessage;

/// <summary>Sven Co-op MapList: virtual map vote list. Delivered to the CMapVotePanel
/// VGUI class (vtable+0x21C) whose handler multiplexes on a command byte:
/// <list type="bullet">
/// <item>0 — full reset: the panel clears its buttons and stores <see cref="TotalMaps"/>.</item>
/// <item>0x7B ('{') — close/hide the vote panel; no further payload.</item>
/// <item>any other value — incremental update: <see cref="StartIndex"/>..<see cref="EndIndex"/>
/// (exclusive) map names follow, one string per entry.</item>
/// </list></summary>
public sealed record ScMapListMessage(byte Command, short TotalMaps, short StartIndex, short EndIndex, string[] MapNames) : IServerMessage
{
    /// <summary>Command 0: reset the list to <see cref="TotalMaps"/> empty slots.</summary>
    public bool IsReset => Command == 0;
    /// <summary>Command 0x7B: hide/close the map vote panel.</summary>
    public bool IsClose => Command == 0x7B;
    /// <summary>Any other command: incremental map-name update.</summary>
    public bool IsUpdate => Command != 0 && Command != 0x7B;
}

/// <summary>Sven Co-op VoteMenu: yes/no vote prompt delivered to the vote panel VGUI
/// class (vtable+0x214). Empty yes/no labels mean the client shows the default
/// "#Menu_Yes" / "#Menu_No" strings.</summary>
public sealed record ScVoteMenuMessage(byte VoteId, string Question, string YesLabel, string NoLabel) : IServerMessage;

/// <summary>Sven Co-op ClExtrasInfo: per-player encrypted authorisation update. The wire
/// format is four length-prefixed blocks framing a CryptoPP authenticated-encryption
/// blob: plain length, IV, encrypted data (12-byte header + body), and a digest whose
/// length must equal the session key length. The key is derived client-side by
/// GenerateKey from the ClServerInfo handshake and never leaves the client, so the
/// payload cannot be decrypted by a third-party implementation (by design — it sets
/// per-player admin levels in the scoreboard table). Only the framing is decoded here;
/// the opaque blocks are exposed for logging/relay purposes.</summary>
public sealed record ScClExtrasInfoMessage(int PlainLength, byte[] Iv, byte[] EncryptedData, byte[] EncryptedDigest) : IServerMessage;

// ─── Messages that were raw tuples in the handler API (now typed) ───

/// <summary>Sven Co-op WeaponSpr: weapon HUD sprite for a slot.</summary>
public sealed record WeaponSpriteMessage(short Slot, string Sprite) : IServerMessage;

/// <summary>Sven Co-op CustWeapon: custom weapon HUD entry for a slot.</summary>
public sealed record CustomWeaponMessage(short Slot, string Sprite) : IServerMessage;

/// <summary>Sven Co-op PrintKB: server-requested key binding text.</summary>
public sealed record KeyBindingMessage(string Action) : IServerMessage;

/// <summary>Sven Co-op NotifyText: HUD notification.</summary>
public sealed record NotifyTextMessage(byte Type, string Text) : IServerMessage;

/// <summary>Sven Co-op EndVote: arrival itself clears the vote UI (no payload).</summary>
public sealed record EndVoteMessage : IServerMessage;

/// <summary>Half-Life ReqState: the game DLL asks for the client's voice state.
/// The profile answers with <c>VModEnable 1</c>.</summary>
public sealed record ReqStateMessage : IServerMessage;
