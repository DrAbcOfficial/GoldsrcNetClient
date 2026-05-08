namespace GoldsrcNetClient.Core.Network;

public interface ISteamAuthProvider
{
    bool IsAvailable { get; }
    byte GetAuthProtocol();
    string GetRawAuthData();
    byte[] GetRawAuthBytes() => System.Text.Encoding.ASCII.GetBytes(GetRawAuthData());
}
