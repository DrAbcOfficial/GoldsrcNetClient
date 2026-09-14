namespace GoldsrcNetClient.Core.Game;

/// <summary>Data for the CurWeapon user message.</summary>
/// <param name="IsActive">1 if the weapon is currently active, 0 otherwise.</param>
/// <param name="WeaponId">Weapon identifier (mod-specific).</param>
/// <param name="ClipAmmo">Ammunition remaining in the current clip/magazine.</param>
public readonly record struct CurWeaponEvent(byte IsActive, byte WeaponId, byte ClipAmmo);

/// <summary>Data for the Damage user message (damage indicator).</summary>
/// <param name="DamageSave">Damage absorbed by armor.</param>
/// <param name="DamageTake">Damage taken to health.</param>
/// <param name="DamageType">Bitwise damage type flags.</param>
/// <param name="OriginX">X coordinate of the damage origin.</param>
/// <param name="OriginY">Y coordinate of the damage origin.</param>
/// <param name="OriginZ">Z coordinate of the damage origin.</param>
public readonly record struct DamageEvent(byte DamageSave, byte DamageTake, int DamageType, float OriginX, float OriginY, float OriginZ);

/// <summary>Data for the DeathMsg user message (Half-Life Deathmatch format).</summary>
/// <param name="KillerId">Player index of the killer.</param>
/// <param name="VictimId">Player index of the victim.</param>
/// <param name="IsHeadshot">1 if a headshot, 0 otherwise. Only present in CS.</param>
/// <param name="WeaponName">Truncated weapon name (no "weapon_" prefix in CS).</param>
public readonly record struct DeathMsgEvent(byte KillerId, byte VictimId, byte IsHeadshot, string WeaponName);

/// <summary>Data for the Health user message.</summary>
/// <param name="Health">Current health value.</param>
public readonly record struct HealthEvent(byte Health);

/// <summary>Data for the Battery (armor) user message.</summary>
/// <param name="Armor">Current armor value.</param>
public readonly record struct BatteryEvent(short Armor);

/// <summary>Data for the AmmoX user message (reserve ammo update).</summary>
/// <param name="AmmoId">Ammo type identifier.</param>
/// <param name="Amount">Amount of ammo available.</param>
public readonly record struct AmmoXEvent(byte AmmoId, byte Amount);

/// <summary>Data for the AmmoPickup user message.</summary>
/// <param name="AmmoId">Ammo type identifier.</param>
/// <param name="Amount">Amount picked up.</param>
public readonly record struct AmmoPickupEvent(byte AmmoId, byte Amount);

/// <summary>Data for the FlashBat user message (flashlight battery).</summary>
/// <param name="ChargePercentage">Battery charge percentage (0-100).</param>
public readonly record struct FlashBatEvent(byte ChargePercentage);

/// <summary>Data for the Flashlight user message.</summary>
/// <param name="IsOn">1 if the flashlight is active, 0 otherwise.</param>
/// <param name="ChargePercent">Battery charge percentage.</param>
public readonly record struct FlashlightEvent(byte IsOn, byte ChargePercent);

/// <summary>Data for the GameMode user message.</summary>
/// <param name="GameMode">Current game mode identifier (0 = undecided/spectator, 1 = singleplayer, etc.).</param>
public readonly record struct GameModeEvent(byte GameMode);

/// <summary>Data for the Geiger user message (radiation indicator).</summary>
/// <param name="Distance">Distance to the hazard.</param>
public readonly record struct GeigerEvent(byte Distance);

/// <summary>Data for the HideWeapon user message.</summary>
/// <param name="Flags">Bitmask of HUD elements to hide.</param>
public readonly record struct HideWeaponEvent(byte Flags);

/// <summary>Data for the HudText user message.</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
public readonly record struct HudTextEvent(string TextCode, byte Style);

/// <summary>Data for the ItemPickup user message.</summary>
/// <param name="ItemName">Name of the picked-up item.</param>
public readonly record struct ItemPickupEvent(string ItemName);

/// <summary>Data for the ScreenFade user message (screen color fading).</summary>
/// <param name="Duration">Fade-in duration in milliseconds.</param>
/// <param name="HoldTime">Duration to hold the fade.</param>
/// <param name="FadeFlags">Fade type flags (1 = fade in, 2 = fade out, 4 = modulate).</param>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
/// <param name="A">Alpha component (0-255).</param>
public readonly record struct ScreenFadeEvent(short Duration, short HoldTime, short FadeFlags, byte R, byte G, byte B, byte A);

/// <summary>Data for the ScreenShake user message.</summary>
/// <param name="Amplitude">Shake amplitude.</param>
/// <param name="Duration">Shake duration in milliseconds.</param>
/// <param name="Frequency">Shake frequency.</param>
public readonly record struct ScreenShakeEvent(short Amplitude, short Duration, short Frequency);

/// <summary>Data for the SetFOV user message.</summary>
/// <param name="Fov">Field of view value (default 90).</param>
public readonly record struct SetFovEvent(byte Fov);

/// <summary>Data for the StatusIcon user message.</summary>
/// <param name="Status">1 to show the icon, 0 to hide.</param>
/// <param name="IconName">Icon sprite name.</param>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
public readonly record struct StatusIconEvent(byte Status, string IconName, byte R, byte G, byte B);

/// <summary>Data for the TeamInfo user message.</summary>
/// <param name="PlayerIndex">Player index (1-based).</param>
/// <param name="TeamName">Team name string.</param>
public readonly record struct TeamInfoEvent(byte PlayerIndex, string TeamName);

/// <summary>Data for the TextMsg user message.</summary>
/// <param name="MsgDest">Message destination (1 = console, 2 = center, 3 = chat, 4 = center no stay, 5 = HUD_PRINTCENTER).</param>
/// <param name="Message">The text message content.</param>
public readonly record struct TextMsgEvent(byte MsgDest, string Message);

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
public readonly record struct WeaponListEvent(string WeaponName, byte Ammo1Id, byte Ammo1Max, byte Ammo2Id, byte Ammo2Max, byte Slot, byte Position, byte WeaponId, byte Flags);

/// <summary>Data for the WeapPickup user message (weapon icon pickup display).</summary>
/// <param name="WeaponName">Name of the weapon that was picked up.</param>
public readonly record struct WeapPickupEvent(string WeaponName);

/// <summary>Data for the SayText user message (chat message).</summary>
/// <param name="SenderId">Player index of the sender (0 for server).</param>
/// <param name="Message">The chat message text.</param>
public readonly record struct SayTextEvent(byte SenderId, string Message);

/// <summary>Data for the Train user message.</summary>
/// <param name="Position">Train control position (0 = inactive, 1 = active).</param>
public readonly record struct TrainEvent(byte Position);

/// <summary>Data for the VGUIMenu user message.</summary>
/// <param name="MenuType">Menu type identifier.</param>
/// <param name="Data">Raw remaining data as a null-terminated string block.</param>
public readonly record struct VguiMenuEvent(byte MenuType, string Data);

/// <summary>Data for the ResetHUD user message.</summary>
public readonly record struct ResetHudEvent();

/// <summary>Data for the InitHUD user message.</summary>
public readonly record struct InitHudEvent();

/// <summary>Data for the GameTitle user message.</summary>
/// <param name="Show">1 to show the game title, 0 to hide.</param>
public readonly record struct GameTitleEvent(byte Show);

// ─── Counter-Strike specific ───

/// <summary>Data for the Money user message (Counter-Strike).</summary>
/// <param name="Amount">Current money amount.</param>
/// <param name="FlashAmount">Number of times the money display should flash (0 means don't flash).</param>
public readonly record struct MoneyEvent(short Amount, byte FlashAmount);

/// <summary>Data for the Radar user message (Counter-Strike).</summary>
/// <param name="PlayerIndex">Player index.</param>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public readonly record struct RadarEvent(byte PlayerIndex, float X, float Y, float Z);

/// <summary>Data for the ScoreInfo user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="Score">Player score.</param>
/// <param name="Deaths">Player deaths.</param>
/// <param name="IsAlive">1 if player is alive, 0 if dead.</param>
/// <param name="TeamId">Team ID.</param>
public readonly record struct ScoreInfoEvent(byte PlayerId, short Score, short Deaths, byte IsAlive, byte TeamId);

/// <summary>Data for the ScoreAttrib user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="Flags">Attribute flags (1 = dead, 2 = bomb carrier, 4 = VIP, 8 = defuser).</param>
public readonly record struct ScoreAttribEvent(byte PlayerId, byte Flags);

/// <summary>Data for the RoundTime user message (Counter-Strike).</summary>
/// <param name="Seconds">Round time remaining in seconds.</param>
public readonly record struct RoundTimeEvent(short Seconds);

/// <summary>Data for the BombDrop user message (Counter-Strike).</summary>
/// <param name="X">Bomb drop X coordinate.</param>
/// <param name="Y">Bomb drop Y coordinate.</param>
/// <param name="Z">Bomb drop Z coordinate.</param>
/// <param name="Planted">1 if the bomb was planted, 0 if dropped.</param>
public readonly record struct BombDropEvent(float X, float Y, float Z, byte Planted);

/// <summary>Data for the BombPickup user message (Counter-Strike).</summary>
public readonly record struct BombPickupEvent();

/// <summary>Data for the HostageK user message (Counter-Strike).</summary>
/// <param name="HostageId">Hostage entity index.</param>
public readonly record struct HostageKEvent(byte HostageId);

/// <summary>Data for the HostagePos user message (Counter-Strike).</summary>
/// <param name="Flag">Update flag (1 on HUD full update).</param>
/// <param name="HostageId">Hostage entity index.</param>
/// <param name="X">X coordinate.</param>
/// <param name="Y">Y coordinate.</param>
/// <param name="Z">Z coordinate.</param>
public readonly record struct HostagePosEvent(byte Flag, byte HostageId, float X, float Y, float Z);

/// <summary>Data for the BarTime user message (Counter-Strike).</summary>
/// <param name="Duration">Progress bar duration in seconds.</param>
public readonly record struct BarTimeEvent(short Duration);

/// <summary>Data for the BarTime2 user message (Counter-Strike).</summary>
/// <param name="Duration">Total duration in seconds.</param>
/// <param name="StartPercent">Starting fill percentage.</param>
public readonly record struct BarTime2Event(short Duration, short StartPercent);

/// <summary>Data for the BlinkAcct user message (Counter-Strike).</summary>
/// <param name="BlinkAmount">Number of times to flash the money display.</param>
public readonly record struct BlinkAcctEvent(byte BlinkAmount);

/// <summary>Data for the ArmorType user message (Counter-Strike).</summary>
/// <param name="HasHelmet">1 to show helmet icon, 0 to hide.</param>
public readonly record struct ArmorTypeEvent(byte HasHelmet);

/// <summary>Data for the Crosshair user message (Counter-Strike).</summary>
/// <param name="Show">1 to show the spectator crosshair, 0 to hide.</param>
public readonly record struct CrosshairEvent(byte Show);

/// <summary>Data for the Fog user message.</summary>
/// <param name="R">Red component.</param>
/// <param name="G">Green component.</param>
/// <param name="B">Blue component.</param>
/// <param name="Density">Fog density.</param>
public readonly record struct FogEvent(byte R, byte G, byte B, byte Density);

/// <summary>Data for the NVGToggle user message (Counter-Strike).</summary>
/// <param name="Mode">1 to enable night vision, 0 to disable.</param>
public readonly record struct NvgToggleEvent(byte Mode);

/// <summary>Data for the ReceiveW user message (Counter-Strike).</summary>
/// <param name="ItemId">Received weapon/item ID.</param>
public readonly record struct ReceiveWEvent(byte ItemId);

/// <summary>Data for the ReloadSound user message (Counter-Strike).</summary>
/// <param name="PlayerIndex">Player index.</param>
/// <param name="WeaponId">Weapon ID being reloaded.</param>
public readonly record struct ReloadSoundEvent(byte PlayerIndex, byte WeaponId);

/// <summary>Data for the SendAudio user message (Counter-Strike).</summary>
/// <param name="Channel">Audio channel.</param>
/// <param name="SoundName">Sound file name.</param>
public readonly record struct SendAudioEvent(byte Channel, string SoundName);

/// <summary>Data for the ShadowIdx user message (Counter-Strike).</summary>
/// <param name="PlayerId">Player index.</param>
/// <param name="ShadowId">Shadow index.</param>
public readonly record struct ShadowIdxEvent(byte PlayerId, byte ShadowId);

/// <summary>Data for the ShowMenu user message (Counter-Strike).</summary>
/// <param name="ValidSlots">Bitmask of valid menu slots.</param>
/// <param name="DisplayTime">Menu display duration in seconds (0 = permanent).</param>
/// <param name="NeedMore">1 if the menu has more pages.</param>
/// <param name="Text">Menu text content.</param>
public readonly record struct ShowMenuEvent(short ValidSlots, byte DisplayTime, byte NeedMore, string Text);

/// <summary>Data for the ShowTimer user message (Counter-Strike).</summary>
/// <param name="Show">1 to show the round timer, 0 to hide.</param>
public readonly record struct ShowTimerEvent(byte Show);

/// <summary>Data for the Spectator user message (Counter-Strike).</summary>
/// <param name="PlayerId">Target player index.</param>
/// <param name="Mode">Spectator mode.</param>
public readonly record struct SpectatorEvent(byte PlayerId, byte Mode);

/// <summary>Data for the TeamScore user message (Counter-Strike).</summary>
/// <param name="TeamName">Team name string.</param>
/// <param name="Score">Team score.</param>
public readonly record struct TeamScoreEvent(string TeamName, short Score);

/// <summary>Data for the VoteMenu user message (Counter-Strike).</summary>
/// <param name="ValidSlots">Bitmask of valid vote options.</param>
/// <param name="DisplayTime">Display duration.</param>
/// <param name="Text">Vote text content.</param>
public readonly record struct VoteMenuEvent(short ValidSlots, byte DisplayTime, string Text);

/// <summary>Data for the AllowSpec user message.</summary>
/// <param name="Allowed">1 if spectating is allowed, 0 otherwise.</param>
public readonly record struct AllowSpecEvent(byte Allowed);

/// <summary>Data for the ForceCam user message (Counter-Strike).</summary>
/// <param name="ForceCamValue">Force camera value.</param>
/// <param name="ForceChaseCamValue">Force chase cam value.</param>
/// <param name="Unknown">Unknown third value.</param>
public readonly record struct ForceCamEvent(byte ForceCamValue, byte ForceChaseCamValue, byte Unknown);

/// <summary>Data for the HLTV user message.</summary>
/// <param name="ClientId">Client or player index.</param>
/// <param name="Flags">HLTV flags.</param>
public readonly record struct HltvEvent(byte ClientId, byte Flags);

/// <summary>Data for the BotVoice user message (Counter-Strike: Condition Zero).</summary>
/// <param name="Status">1 if talking, 0 otherwise.</param>
/// <param name="PlayerIndex">Player index.</param>
public readonly record struct BotVoiceEvent(byte Status, byte PlayerIndex);

/// <summary>Data for the BuyClose user message (Counter-Strike).</summary>
public readonly record struct BuyCloseEvent();

/// <summary>Data for the ADStop user message (Counter-Strike).</summary>
public readonly record struct AdStopEvent();

/// <summary>Data for the ItemStatus user message (Counter-Strike).</summary>
/// <param name="ItemBits">Bitmask of carried items.</param>
public readonly record struct ItemStatusEvent(int ItemBits);

/// <summary>Data for the HudTextArgs user message (Counter-Strike).</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
/// <param name="Args">Sub-message strings.</param>
public readonly record struct HudTextArgsEvent(string TextCode, byte Style, string[] Args);

/// <summary>Data for the HudTextPro user message (Counter-Strike).</summary>
/// <param name="TextCode">Text reference code from titles.txt.</param>
/// <param name="Style">Display style.</param>
public readonly record struct HudTextProEvent(string TextCode, byte Style);

/// <summary>Data for the HudColor user message (Half-Life: Opposing Force).</summary>
/// <param name="R">Red component (0-255).</param>
/// <param name="G">Green component (0-255).</param>
/// <param name="B">Blue component (0-255).</param>
public readonly record struct HudColorEvent(byte R, byte G, byte B);

/// <summary>Data for the Concuss user message (concussion effect).</summary>
/// <param name="Amount">Concussion intensity.</param>
public readonly record struct ConcussEvent(byte Amount);

/// <summary>Data for raw/unparsed user messages.</summary>
/// <param name="Index">Message type index.</param>
/// <param name="Name">Message name from SVC_NEWUSERMSG.</param>
/// <param name="Data">Raw remaining data bytes.</param>
public readonly record struct RawUserMessage(byte Index, string Name, byte[] Data);

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
public readonly record struct ScCurWeaponEvent(byte IsActive, short WeaponId, int ClipAmmo, int ReserveAmmo);

/// <summary>Sven Co-op Health: 32-bit health.</summary>
public readonly record struct ScHealthEvent(int Health);

/// <summary>Sven Co-op Battery (armor): single byte.</summary>
public readonly record struct ScBatteryEvent(byte Armor);

/// <summary>Sven Co-op AmmoX: 32-bit reserve count.</summary>
public readonly record struct ScAmmoXEvent(byte AmmoId, int Amount);

/// <summary>Sven Co-op AmmoPickup: 32-bit count.</summary>
public readonly record struct ScAmmoPickupEvent(byte AmmoId, int Amount);

/// <summary>Sven Co-op WeapPickup: weapon id as a short (Half-Life sends a name string).</summary>
public readonly record struct ScWeapPickupEvent(short WeaponId);

/// <summary>Sven Co-op WeaponList: primary/secondary ammo maxima are 32-bit.</summary>
public readonly record struct ScWeaponListEvent(
    string WeaponName, byte PrimaryAmmoId, int PrimaryAmmoMax,
    byte SecondaryAmmoId, int SecondaryAmmoMax,
    byte Slot, byte Position, short WeaponId, byte Flags);

/// <summary>Sven Co-op TextMsg: destination plus the message and its four format parameters.</summary>
public readonly record struct ScTextMsgEvent(byte MsgDest, string Message, string Param1, string Param2, string Param3, string Param4);

/// <summary>Sven Co-op HudText: a single text/localisation string.</summary>
public readonly record struct ScHudTextEvent(string Text);

/// <summary>Sven Co-op Concuss: direction vector of the concussion effect.</summary>
public readonly record struct ScConcussEvent(float DirectionX, float DirectionY, float DirectionZ);

/// <summary>Sven Co-op Fog (format differs from the Counter-Strike Fog message).</summary>
/// <param name="Leading">Leading short read by the client but not stored.</param>
/// <param name="Enabled">Fog toggle.</param>
/// <param name="OriginX/Y/Z">Coordinates read by the client but not stored.</param>
/// <param name="Unknown">Short read between the coordinates and the colour bytes.</param>
/// <param name="R/G/B">Fog colour.</param>
/// <param name="Value1/Value2">Trailing shorts (fog range/density candidates).</param>
public readonly record struct ScFogEvent(bool Enabled, float OriginX, float OriginY, float OriginZ,
    short Unknown, byte R, byte G, byte B, short Value1, short Value2);

/// <summary>Sven Co-op ShowMenu: byte-sized slot mask, signed display time, flag byte.</summary>
public readonly record struct ScShowMenuEvent(byte ValidSlots, sbyte DisplayTime, byte Flags, string Text);

/// <summary>Sven Co-op HideHUD: 16-bit hide mask (Half-Life's HideWeapon uses a byte).</summary>
public readonly record struct ScHideHudEvent(short Flags);

/// <summary>VoiceMask: per-player voice audibility/ban bitmasks.</summary>
public readonly record struct VoiceMaskEvent(int AudiblePlayers, int BannedPlayers, byte Flags);

/// <summary>Sven Co-op ViewMode: camera perspective.</summary>
public readonly record struct ScViewModeEvent(bool ThirdPerson);

/// <summary>Sven Co-op CdAudio: MP3 track request (0 = stop, 1..30 → media/Half-LifeXX).</summary>
public readonly record struct ScCdAudioEvent(byte Track);

/// <summary>Sven Co-op ClassicMode: classic gameplay mode toggle.</summary>
public readonly record struct ScClassicModeEvent(bool Enabled);

/// <summary>Sven Co-op VModelPos: view model offset.</summary>
public readonly record struct ScVModelPosEvent(bool Enabled, float X, float Y, float Z);

/// <summary>Sven Co-op TimeEnd: round timer end (absolute client time).</summary>
public readonly record struct ScTimeEndEvent(int Seconds);

/// <summary>Sven Co-op OnTank: player is driving a func tank.</summary>
public readonly record struct ScOnTankEvent(bool OnTank);

/// <summary>Sven Co-op Playlist: media playlist switch.</summary>
public readonly record struct ScPlaylistEvent(string Playlist);

/// <summary>Sven Co-op Speaksent: server-requested sentence playback.</summary>
public readonly record struct ScSentenceEvent(string Sentence);

/// <summary>Sven Co-op ValClass: five class/slot values.</summary>
public readonly record struct ScValClassEvent(short[] Classes);

/// <summary>One team entry of the Sven Co-op TeamNames message.</summary>
public readonly record struct ScTeamNamesTeam(string Name, float ColorR, float ColorG, float ColorB);

/// <summary>Sven Co-op TeamNames: team list with colours.</summary>
public readonly record struct ScTeamNamesEvent(ScTeamNamesTeam[] Teams);

/// <summary>Sven Co-op MOTD chunk.</summary>
public readonly record struct ScMotdEvent(bool IsFinal, string Text);

/// <summary>Sven Co-op ServerName: server hostname (client overrides non-Sven names).</summary>
public readonly record struct ScServerNameEvent(string ServerName);

/// <summary>Sven Co-op ServerVer: server version string (mismatch disconnects the client).</summary>
public readonly record struct ScServerVersionEvent(string Version);

/// <summary>Sven Co-op ServerBuild: server build string.</summary>
public readonly record struct ScServerBuildEvent(string Build);

/// <summary>Sven Co-op NextMap.</summary>
public readonly record struct ScNextMapEvent(string MapName);

/// <summary>Sven Co-op ScoreInfo: float-based scoreboard entry (format differs from CS).</summary>
public readonly record struct ScScoreInfoEvent(byte PlayerIndex, float Score, int UnknownLong,
    float UnknownFloat1, float UnknownFloat2, byte ClassId, byte UnknownByte1, byte UnknownByte2);

/// <summary>Sven Co-op TeamScore: two score values per team.</summary>
public readonly record struct ScTeamScoreEvent(string TeamName, short Score, short Score2);

/// <summary>Sven Co-op Gib: gib burst at a point with a velocity bias.</summary>
public readonly record struct ScGibEvent(byte Type, float OriginX, float OriginY, float OriginZ,
    float VelocityX, float VelocityY, float VelocityZ);

/// <summary>Sven Co-op TE_CUSTOM: subtyped temporary effect.</summary>
public readonly record struct ScTeCustomEvent(byte SubType, short Id, short Count,
    float OriginX, float OriginY, float OriginZ, byte Value);

/// <summary>Sven Co-op CbElec: electrified tripwire state (wire byte: bit0-4 entity, bit6 active).</summary>
public readonly record struct ScCbElecEvent(bool On, byte Entity);

/// <summary>Sven Co-op ShkFlash: shock roach flash at an origin (mode 0 = fire, else impact).</summary>
public readonly record struct ScShkFlashEvent(float X, float Y, float Z, byte Mode);

/// <summary>Sven Co-op TracerDecal: tracer endpoints and decal type.</summary>
public readonly record struct ScTracerDecalEvent(float StartX, float StartY, float StartZ,
    float EndX, float EndY, float EndZ, byte DecalType, byte Unknown);

/// <summary>Sven Co-op SporeTrail: spore trail attach state for an entity.</summary>
public readonly record struct ScSporeTrailEvent(short Entity, bool Attach);

/// <summary>Sven Co-op CreateBlood (a.k.a. SpawnBlood): blood effect.</summary>
public readonly record struct ScCreateBloodEvent(float X, float Y, float Z, byte Color, byte Amount);

/// <summary>Sven Co-op GargSplash: gargantua splash with colour triple (the client
/// reads coordinates and takes their absolute value before use).</summary>
public readonly record struct ScGargSplashEvent(float X, float Y, float Z, float ColorR, float ColorG, float ColorB);

/// <summary>Sven Co-op StartSound: flag-driven sound playback. Optional fields are present
/// only when the corresponding flag bit is set (0x10 entity, 0x1 volume, 0x2 attenuation,
/// 0x4 pitch, 0x8 origin, 0x8000 duration). Channel and sound index are always sent.</summary>
public readonly record struct ScStartSoundEvent(short Flags, short? Entity, byte? Volume, byte? Attenuation,
    byte? Pitch, float? OriginX, float? OriginY, float? OriginZ, float? Duration, byte Channel, short SoundIndex);

/// <summary>Sven Co-op ToxicCloud.</summary>
public readonly record struct ScToxicCloudEvent(float X, float Y, float Z);

/// <summary>Sven Co-op SRDetonate: shock roach detonation.</summary>
public readonly record struct ScSrDetonateEvent(float X, float Y, float Z, byte Radius);

/// <summary>Sven Co-op SRPrimed: shock roach fuse.</summary>
public readonly record struct ScSrPrimedEvent(byte Entity, float Time);

/// <summary>Sven Co-op SRPrimedOff: cancels a primed shock roach.</summary>
public readonly record struct ScSrPrimedOffEvent(byte Entity);

/// <summary>Sven Co-op RampSprite: highly configurable sprite ramp effect. Optional fields
/// are present when the corresponding flag bit is set: 0x1 StartTime, 0x2 FadeIn, 0x4 FadeOut,
/// 0x8 RenderModeFx (low 5 bits mode, high 3 bits fx), 0x10/0x20/0x40 RGB, 0x8000 trailing
/// byte, 0x100 secondary colour (3 coords), 0x200-0x4000 further scale bytes.</summary>
public readonly record struct ScRampSpriteEvent(short Entity, byte LifeTicks, float X, float Y, float Z, short Flags,
    byte? StartTime, byte? FadeInTime, byte? FadeOutTime, byte? RenderModeFx,
    byte? R, byte? G, byte? B, byte? UnknownBit15,
    float? Color2R, float? Color2G, float? Color2B,
    byte? UnknownBit200, byte? UnknownBit400, byte? UnknownBit800, byte? UnknownBit1000,
    byte? UnknownBit2000, byte? UnknownBit4000, byte? UnknownBit8000);

/// <summary>Sven Co-op ShieldRic: shield ricochet spark.</summary>
public readonly record struct ScShieldRicEvent(float X, float Y, float Z);

/// <summary>Sven Co-op WeatherFX: full weather particle description. The trailing short/byte/
/// float groups are weather parameters whose per-field semantics the client passes through
/// opaquely, so they keep neutral names.</summary>
public readonly record struct ScWeatherFxEvent(short Type,
    float MinsX, float MinsY, float MinsZ, float MaxsX, float MaxsY, float MaxsZ,
    float AngleX, float AngleY, float AngleZ,
    short UnknownShort1, float Float1, byte Byte1, short UnknownShort2, float Float2,
    byte Byte2, byte Byte3, float Float3, byte Byte4, byte Byte5, byte Byte6, byte Byte7,
    float Float4, float Float5, float Float6, float Float7);

/// <summary>Sven Co-op CameraMouse: fixed camera mode; mode 2 carries a parameter string.</summary>
public readonly record struct ScCameraMouseEvent(byte Mode, string Parameter);

/// <summary>Sven Co-op Flamethwr: flame thrower flame segment.</summary>
public readonly record struct ScFlamethrowerEvent(byte Entity, float StartX, float StartY, float StartZ,
    float EndX, float EndY, float EndZ);

/// <summary>Sven Co-op ChangeSky: sky box name and colour (all -1 means keep default).</summary>
public readonly record struct ScChangeSkyEvent(string SkyName, float ColorR, float ColorG, float ColorB);

/// <summary>Sven Co-op ToggleElem: HUD element channel on/off.</summary>
public readonly record struct ScToggleElemEvent(byte Channel, bool State);

/// <summary>Sven Co-op CustSpr (a.k.a. CustomSprite): custom HUD sprite element.</summary>
public readonly record struct ScCustomSpriteEvent(byte Channel, int Flags, string Sprite, byte X, byte Y,
    short Width, short Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    byte Unknown1, byte Unknown2, float Float1, float Float2, float Float3, float Float4, float Float5, byte Unknown3);

/// <summary>Sven Co-op NumDisplay: custom numeric HUD element.</summary>
public readonly record struct ScNumDisplayEvent(byte Channel, int Flags, float Value, byte X, byte Y,
    float Width, float Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    string Font, byte RegionX, byte RegionY, short RegionWidth, short RegionHeight,
    float Float1, float Float2, float Float3, float Float4, byte Unknown);

/// <summary>Sven Co-op UpdateNum: value update for a numeric HUD element.</summary>
public readonly record struct ScUpdateNumEvent(byte Channel, float Value);

/// <summary>Sven Co-op TimeDisplay: custom timer HUD element.</summary>
public readonly record struct ScTimeDisplayEvent(byte Channel, int Flags, float Value1, float Value2,
    float Width, float Height, byte R1, byte G1, byte B1, byte A1, byte R2, byte G2, byte B2, byte A2,
    string Font, byte RegionX, byte RegionY, short RegionWidth, short RegionHeight,
    float Float1, float Float2, float Float3, float Float4, byte Unknown);

/// <summary>Sven Co-op UpdateTime: time update for a timer HUD element.</summary>
public readonly record struct ScUpdateTimeEvent(byte Channel, float Time, float Duration);

/// <summary>Sven Co-op InvAdd: inventory item added. The five strings are the item's
/// name/icon/description texts (client stores them opaquely).</summary>
public readonly record struct ScInventoryAddEvent(int Id, bool Flag1, bool Flag2, bool Flag3, float Time,
    string String1, string String2, string String3, string String4, string String5);

/// <summary>Sven Co-op InvRemove: inventory items removed (id 0 = all matching).</summary>
public readonly record struct ScInventoryRemoveEvent(int Id, byte Flags);

/// <summary>Sven Co-op PrtlUpdt (a.k.a. PortalUpdate): portal/monitor entity update with
/// conditional trailing sections per type/style.</summary>
public readonly record struct ScPortalUpdateEvent(bool Removed, int Entity,
    float[] Vec1, float[] Vec2, byte Type, byte Style, float Life,
    byte Byte1, int Long1, int Long2, bool Flag1,
    int? ModelIndex, float[]? ModelOrigin, float[]? ModelAngles,
    int? Long3, int? Long4, bool? FlagA, bool? FlagB, string? Name);

/// <summary>Sven Co-op ClServerInfo: server info handshake (key feeds GenerateKey).</summary>
public readonly record struct ScClServerInfoEvent(byte Flag, int Value, string Key);
