// ============================================================================
// ResourceType — Every known Sims 4 DBPF resource type ID.
// Values are the 32-bit type hashes stored in the index.
// ============================================================================

namespace Sims4.Dbpf.Enums;

public enum ResourceType : uint
{
    Unknown = 0x00000000,

    // ---- Geometry / Mesh ----
    GEOM            = 0x015A1849,
    MODL            = 0x01661233,
    MLOD            = 0x01D10F34,
    BGEO            = 0x067CAA11,
    BOND            = 0x0355E0A6,

    // ---- RCOL Embedded Chunks ----
    VBUF            = 0x01D0E6FB,
    IBUF            = 0x01D0E70F,
    VRTF            = 0x01D0E723,
    SKIN            = 0x01D0E76B,
    VBUF_SHADOW     = 0x0229684B,
    IBUF_SHADOW     = 0x0229684F,

    // ---- Material / Shader ----
    MATD            = 0x01D0E75D,
    MTST            = 0x02019972,
    TXTC            = 0x033A1435,
    TXTC2           = 0x0341ACC9,

    // ---- Object Linking / Scene ----
    VPXY            = 0x736884F1,
    RSLT            = 0xD3044521,
    FTPT            = 0xD382BF57,
    LITE            = 0x03B4C61D,
    WMAP            = 0x1CC04273,
    KDTR            = 0x033B2B66,
    ANIM            = 0x63A33EA7,

    // ---- Catalog / Build-Buy ----
    OBJD            = 0xC0DB5AE7,
    COBJ            = 0x319E4F1D,
    CWAL            = 0xD5F0F921,
    CFLR            = 0xB4F762C9,
    CFEN            = 0x0418FE2A,
    CFND            = 0x2FAE983E,
    CCOL            = 0x1D6DF1CF,
    CBLK            = 0x07936CE0,
    CRTR            = 0xB0311D0F,
    CRPT            = 0xF1EDBD86,
    CSTL            = 0x9F5CFF10,
    CSTR            = 0x9A20CD1C,
    CTPT            = 0xEBCBB16C,
    CRAL            = 0x1C1CF1F7,
    CFTR            = 0xE7ADA79D,
    CPLT            = 0xA5DFFCF3,
    CFRZ            = 0xA057811C,
    CFLT            = 0x84C23219,
    STRM            = 0x74050B1F,
    RFST            = 0x91EDBD3E,
    MODULAR         = 0xCF9A4ACE,

    // ---- CAS ----
    CASP            = 0x034AEECB,
    TONE            = 0x0354796A,
    BONE            = 0x00AE6C67,
    GEOL            = 0xAC16FBEC,
    SIMD            = 0xC5F6763E,
    DFMP            = 0xDB43E069,
    SCLT            = 0x9D1AB874,
    HSPC            = 0x8B18FF6E,
    STYL            = 0x71BDB8A2,
    SIMP            = 0x105205BA,
    CASPRESET       = 0xEAA32ADD,
    ANMC            = 0xC4DFAE6D,

    // ---- Textures ----
    DST             = 0x00B2D882,
    DST2            = 0xB6C8B6A0,
    RLE2            = 0x3453CF95,
    RLES            = 0xBA856C78,
    LRLE            = 0x2BC04EDF,

    // ---- Thumbnails / Images ----
    THUM            = 0x3C2A8647,
    IMG_DST         = 0x3C1AF1F2,
    IMAG_JPG        = 0x2F7D0002,
    IMAG_PNG        = 0x2F7D0004,

    // ---- Animation ----
    CLIP            = 0x6B20C4F3,
    JAZZ            = 0x02D5DF13,

    // ---- Sim / Outfit ----
    SIMO            = 0x025ED6F4,

    // ---- Strings / Data ----
    STBL            = 0x220557DA,
    NMAP            = 0x0166038C,
    OBJK            = 0x02DC343F,
    XML             = 0x0333406C,
    XML_TUNING      = 0x03B33DDF,
    RIG             = 0x8EAF13DE,
    AUEV            = 0xBDD82221,
    AUD_SNR         = 0x01A527DB,
    AUD_SNS         = 0x01EEF63A,
    MTBL            = 0x81CA1A10,
    TUNING          = 0xFD04E3BE,

    // ---- Index ----
    DIR_TYPE        = 0xE86B1EEF,
}
