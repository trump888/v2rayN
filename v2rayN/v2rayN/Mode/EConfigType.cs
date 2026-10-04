
namespace v2rayN.Mode
{
    public enum EConfigType
    {
        VMess = 1,
        Custom = 2,
        Shadowsocks = 3,
        Socks = 4,
        VLESS = 5,
        Trojan = 6,
        Hysteria2 = 7,
        TUIC = 8,
        WireGuard = 9,
        HTTP = 10,
        Mieru = 11,
        // 12 matches the value 7.x uses, so a profile exported from the newer
        // build keeps its meaning. Only the enum value so far: the sing-box
        // generator exists (Handler/SingboxConfigHandler.cs) and is validated
        // against `sing-box check` in CI, but the AddServerForm UI and the
        // share-link parser are not wired up yet, so this type is not offered.
        AnyTLS = 12
    }
}
