
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
        // 12 is deliberately NOT the value 7.x uses for AnyTLS (11). 11 is Mieru
        // here, and these values are persisted in existing server configurations,
        // so renumbering Mieru to make AnyTLS match would repoint every saved
        // Mieru profile. The two builds cannot exchange raw configs anyway
        // (different models); share links are the interchange format and those
        // are protocol-based, so the number only has to be stable within this
        // build. 12 is unused here.
        AnyTLS = 12,
        // 13 is unused in this build. 7.x uses 12 for Naive, but 12 is AnyTLS
        // here; same reasoning as AnyTLS = 12 not being 11.
        Naive = 13
    }
}
