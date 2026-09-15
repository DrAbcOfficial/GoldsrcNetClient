using GoldsrcNetClient.Core.Delta;
using GoldsrcNetClient.Core.Protocol;

namespace GoldsrcNetClient.Core.Messages.Engine;

/// <summary>svc_print: text printed to the client console (chat, kick notices, rules).</summary>
public sealed record PrintMessage(string Text) : IServerMessage;

/// <summary>svc_centerprint: text displayed centered on screen.</summary>
public sealed record CenterPrintMessage(string Text) : IServerMessage;

/// <summary>svc_stufftext: a console command the client must execute.</summary>
public sealed record StuffTextMessage(string Command) : IServerMessage;

/// <summary>svc_disconnect: the server dropped the client with a reason. Rest of the packet is discarded.</summary>
public sealed record DisconnectMessage(string Reason) : IServerMessage;

/// <summary>
/// svc_serverinfo: the signon banner. <see cref="WorldmapCrc"/> is already
/// un-munged (Valve branches decrypt it with the player-slot key; plaintext
/// branches pass it through). The raw wire struct is kept in
/// <see cref="Data"/> for diagnostics.
/// </summary>
public sealed record ServerInfoMessage(
    ServerInfoData Data,
    string GameDir,
    string ClientDllName,
    string MapName,
    string GameDescription,
    uint WorldmapCrc) : IServerMessage;

/// <summary>svc_deltadescription: one live delta table definition (always wins over the compiled fallbacks).</summary>
public sealed record DeltaDescriptionMessage(string Name, DeltaType Type) : IServerMessage;

/// <summary>svc_newmovevars: movement physics variables.</summary>
public sealed record NewMoveVarsMessage(NewMoveVarsData Data) : IServerMessage;

/// <summary>svc_newusermsg: the server registered a user message (index ↔ name ↔ declared size).</summary>
public sealed record NewUserMsgMessage(byte Index, string Name, byte DeclaredSize) : IServerMessage;

/// <summary>svc_updateuserinfo: the server broadcast one player's userinfo.</summary>
public sealed record UpdateUserInfoMessage(byte Slot, int UserId, string UserInfo, byte[] CdKeyHash) : IServerMessage;

/// <summary>svc_resourcerequest: the server asks which resources the client needs (carries the spawn count).</summary>
public sealed record ResourceRequestMessage(uint SpawnCount, uint StartIndex) : IServerMessage;

/// <summary>svc_resourcelist: everything the server expects the client to have; <see cref="RawBytes"/> is the bit-packed payload for echo-back.</summary>
public sealed record ResourceListMessage(ResourceInfo[] Resources, byte[] RawBytes) : IServerMessage;

/// <summary>svc_spawnbaseline: delta-compressed entity baselines (parsed opaquely — count only).</summary>
public sealed record SpawnBaselineMessage(int EntityCount) : IServerMessage;

/// <summary>svc_clientdata: the local player's delta state (parsed opaquely).</summary>
public sealed record ClientDataMessage(int WeaponDeltas) : IServerMessage;

/// <summary>svc_signonnum: signon stage counter; 1 completes the signon.</summary>
public sealed record SignOnNumMessage(byte Value) : IServerMessage;

/// <summary>svc_voiceinit: voice codec negotiation.</summary>
public sealed record VoiceInitMessage(string Codec, byte Quality) : IServerMessage;

/// <summary>svc_sound: one world sound event.</summary>
public sealed record SoundMessage(uint Channel, uint Entity, uint SoundNumber, uint FieldMask) : IServerMessage;

/// <summary>svc_customization: one player custom content (decal/spray) registration.</summary>
public sealed record CustomizationMessage(byte PlayerSlot, byte ResourceType, string ResourceName) : IServerMessage;

/// <summary>svc_event / svc_event_reliable: game events (parsed opaquely).</summary>
public sealed record EventMessage(uint EventIndex, bool Reliable) : IServerMessage;

/// <summary>svc_sendcvarvalue: the server queries a cvar by name.</summary>
public sealed record SendCvarValueMessage(string Name) : IServerMessage;

/// <summary>svc_sendcvarvalue2: the server queries a cvar by name with a request id.</summary>
public sealed record SendCvarValue2Message(uint RequestId, string Name) : IServerMessage;

/// <summary>svc_setpause: pause state (read at bit level on the wire).</summary>
public sealed record SetPauseMessage(bool Paused) : IServerMessage;

/// <summary>svc_version: server protocol version.</summary>
public sealed record VersionMessage(uint Protocol) : IServerMessage;

/// <summary>svc_time: server time.</summary>
public sealed record TimeMessage(float Time) : IServerMessage;

/// <summary>svc_timescale: server time scale.</summary>
public sealed record TimeScaleMessage(float Scale) : IServerMessage;

/// <summary>svc_lightstyle: one light style string.</summary>
public sealed record LightStyleMessage(byte Style, string Value) : IServerMessage;

/// <summary>svc_resourcelocation: the download URL for missing resources.</summary>
public sealed record ResourceLocationMessage(string Url) : IServerMessage;

/// <summary>svc_finale: finale text.</summary>
public sealed record FinaleMessage(string Text) : IServerMessage;

/// <summary>svc_cutscene: cutscene name.</summary>
public sealed record CutsceneMessage(string Name) : IServerMessage;

/// <summary>svc_filetxferfailed: a file transfer failed.</summary>
public sealed record FileTxferFailedMessage(string FileName) : IServerMessage;

/// <summary>svc_sendextrainfo: gamedir + sv_cheats flag.</summary>
public sealed record SendExtraInfoMessage(string GameDir, bool SvCheats) : IServerMessage;

/// <summary>svc_exec: client-side config exec (type 1 adds a TFC class number).</summary>
public sealed record ExecMessage(byte Type, byte Class) : IServerMessage;

/// <summary>svc_cdtrack: background music track selection.</summary>
public sealed record CdTrackMessage(byte Track, byte LoopTrack) : IServerMessage;

/// <summary>svc_weaponanim: view-model animation.</summary>
public sealed record WeaponAnimMessage(byte Anim, byte Body) : IServerMessage;

/// <summary>
/// Any engine message whose payload this client consumes without interpreting:
/// the no-payload slots (svc_nop, svc_choke, ...), fixed-size skips (svc_setangle, ...),
/// and the opaque flood messages (svc_packetentities, svc_restore, ...). Subscribing
/// to this type observes the full message stream for diagnostics.
/// </summary>
public sealed record OpaqueEngineMessage(byte Type) : IServerMessage;

/// <summary>svc_tempentity: a temporary entity effect, skipped on the wire (effect type not retained).</summary>
public sealed record TempEntityMessage : IServerMessage;

/// <summary>svc_damage: HUD damage indicator (armor/blood/origin), skipped on the wire.</summary>
public sealed record DamageMessage : IServerMessage;

/// <summary>svc_pings: scoreboard ping/loss updates, skipped on the wire.</summary>
public sealed record PingsMessage : IServerMessage;
