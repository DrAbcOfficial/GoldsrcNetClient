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
