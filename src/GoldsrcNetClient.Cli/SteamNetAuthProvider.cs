using GoldsrcNetClient.Core.Network;
using System.Runtime.InteropServices;
using Steamworks;

namespace GoldsrcNetClient.Cli;

public sealed class SteamNetAuthProvider : ISteamAuthProvider, IDisposable
{
    private static class Native
    {
        [DllImport("steam_api64", CallingConvention = CallingConvention.Cdecl)]
        public static extern int SteamAPI_ISteamUser_InitiateGameConnection(
            byte[] pAuthBlob, int cbMaxAuthBlob,
            ulong steamIDGameServer, uint unIPServer, ushort usPortServer,
            [MarshalAs(UnmanagedType.I1)] bool bSecure);
    }

    private HAuthTicket _hTicket;
    private byte[] _ticketData = [];

    public bool IsAvailable => _ticketData.Length > 0;
    public string? LastError { get; private set; }

    public SteamNetAuthProvider(uint appId = 70)
    {
        try
        {
            Environment.SetEnvironmentVariable("SteamAppId", appId.ToString());
            if (!SteamAPI.Init())
            {
                LastError = $"SteamAPI.Init() returned false. Ensure Steam client is running and you own AppID {appId}.";
                return;
            }

            var ticketBuf = new byte[4096];
            var netIdentity = new SteamNetworkingIdentity();
            _hTicket = SteamUser.GetAuthSessionTicket(ticketBuf, ticketBuf.Length, out uint ticketSize, ref netIdentity);
            if (ticketSize > 0)
            {
                _ticketData = ticketBuf[..(int)ticketSize];
            }
            else
            {
                LastError = "GetAuthSessionTicket returned zero-size ticket.";
            }
        }
        catch (DllNotFoundException ex)
        {
            LastError = $"Native Steam library not found: {ex.Message}. Ensure steam_api64.dll is present in the output directory.";
        }
        catch (Exception ex)
        {
            LastError = $"Steam init exception: {ex.GetType().Name}: {ex.Message}";
        }
    }

    public byte GetAuthProtocol() => 3;

    public string GetRawAuthData()
    {
        if (_ticketData.Length > 0)
            return Convert.ToHexString(_ticketData).ToLowerInvariant();

        return "steam";
    }

    public byte[] GetRawAuthBytes()
    {
        if (_ticketData.Length > 0)
            return _ticketData;

        return System.Text.Encoding.ASCII.GetBytes("steam");
    }

    public byte[] GetGameAuthBytes(ulong serverSteamId, uint serverIp, ushort serverPort)
    {
        if (!IsAvailable)
            return GetRawAuthBytes();

        try
        {
            var blob = new byte[4096];
            int resultLen = Native.SteamAPI_ISteamUser_InitiateGameConnection(
                blob, blob.Length, serverSteamId, serverIp, serverPort, false);

            if (resultLen > 0)
            {
                _ticketData = blob[..resultLen];
                return _ticketData;
            }

            LastError = $"InitiateGameConnection returned {resultLen} (expected > 0). Falling back to session ticket.";
        }
        catch (EntryPointNotFoundException)
        {
            LastError = "InitiateGameConnection not available in this steam_api64.dll (requires Steamworks SDK 1.60+). Falling back to GetAuthSessionTicket.";
        }

        return GetRawAuthBytes();
    }

    public void Dispose()
    {
        if (_hTicket != HAuthTicket.Invalid)
        {
            SteamUser.CancelAuthTicket(_hTicket);
            _hTicket = HAuthTicket.Invalid;
        }
        _ticketData = [];
        try { SteamAPI.Shutdown(); } catch { }
    }
}
