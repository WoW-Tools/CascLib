using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace CASCLib
{
    public class KeyService
    {
        private static Dictionary<ulong, byte[]> keys = new Dictionary<ulong, byte[]>()
        {
            // hardcoded Overwatch keys
            [0xFB680CB6A8BF81F3] = "62D90EFA7F36D71C398AE2F1FE37BDB9".FromHexString(),
            [0x402CD9D8D6BFED98] = "AEB0EADEA47612FE6C041A03958DF241".FromHexString(),
            // streamed Overwatch keys
            [0xDBD3371554F60306] = "34E397ACE6DD30EEFDC98A2AB093CD3C".FromHexString(),
            [0x11A9203C9881710A] = "2E2CB8C397C2F24ED0B5E452F18DC267".FromHexString(),
            [0xA19C4F859F6EFA54] = "0196CB6F5ECBAD7CB5283891B9712B4B".FromHexString(),
            [0x87AEBBC9C4E6B601] = "685E86C6063DFDA6C9E85298076B3D42".FromHexString(),
            [0xDEE3A0521EFF6F03] = "AD740CE3FFFF9231468126985708E1B9".FromHexString(),
            [0x8C9106108AA84F07] = "53D859DDA2635A38DC32E72B11B32F29".FromHexString(),
            [0x49166D358A34D815] = "667868CD94EA0135B9B16C93B1124ABA".FromHexString(),
            [0x1463A87356778D14] = "69BD2A78D05C503E93994959B30E5AEC".FromHexString(),
            [0x5E152DE44DFBEE01] = "E45A1793B37EE31A8EB85CEE0EEE1B68".FromHexString(),
            [0x9B1F39EE592CA415] = "54A99F081CAD0D08F7E336F4368E894C".FromHexString(),
            [0x24C8B75890AD5917] = "31100C00FDE0CE18BBB33F3AC15B309F".FromHexString(),
            [0xEA658B75FDD4890F] = "DEC7A4E721F425D133039895C36036F8".FromHexString(),
            [0x026FDCDF8C5C7105] = "8F41809DA55366AD416D3C337459EEE3".FromHexString(),
            [0xCAE3FAC925F20402] = "98B78E8774BF275093CB1B5FC714511B".FromHexString(),
            [0x061581CA8496C80C] = "DA2EF5052DB917380B8AA6EF7A5F8E6A".FromHexString(),
            [0xBE2CB0FAD3698123] = "902A1285836CE6DA5895020DD603B065".FromHexString(),
            [0x57A5A33B226B8E0A] = "FDFC35C99B9DB11A326260CA246ACB41".FromHexString(),
            [0x42B9AB1AF5015920] = "C68778823C964C6F247ACC0F4A2584F8".FromHexString(),
            [0x4F0FE18E9FA1AC1A] = "89381C748F6531BBFCD97753D06CC3CD".FromHexString(),
            [0x7758B2CF1E4E3E1B] = "3DE60D37C664723595F27C5CDBF08BFA".FromHexString(),
            [0xE5317801B3561125] = "7DD051199F8401F95E4C03C884DCEA33".FromHexString(),
            [0x16B866D7BA3A8036] = "1395E882BF25B481F61A4D621141DA6E".FromHexString(),
            [0x11131FFDA0D18D30] = "C32AD1B82528E0A456897B3CE1C2D27E".FromHexString(),
            [0xCAC6B95B2724144A] = "73E4BEA145DF2B89B65AEF02F83FA260".FromHexString(),
            [0xB7DBC693758A5C36] = "BC3A92BFE302518D91CC30790671BF10".FromHexString(),
            [0x90CA73B2CDE3164B] = "5CBFF11F22720BACC2AE6AAD8FE53317".FromHexString(),
            [0x6DD3212FB942714A] = "E02C1643602EC16C3AE2A4D254A08FD9".FromHexString(),
            [0x11DDB470ABCBA130] = "66198766B1C4AF7589EFD13AD4DD667A".FromHexString(),
            [0x5BEF27EEE95E0B4B] = "36BCD2B551FF1C84AA3A3994CCEB033E".FromHexString(),
            [0x9359B46E49D2DA42] = "173D65E7FCAE298A9363BD6AA189F200".FromHexString(),
            [0x1A46302EF8896F34] = "8029AD5451D4BC18E9D0F5AC449DC055".FromHexString(),
            [0x693529F7D40A064C] = "CE54873C62DAA48EFF27FCC032BD07E3".FromHexString(),
            [0x388B85AEEDCB685D] = "D926E659D04A096B24C19151076D379A".FromHexString(),
            // streamed WoW keys
            [0xFA505078126ACB3E] = "BDC51862ABED79B2DE48C8E7E66C6200".FromHexString(), // TactKeyId 15
            [0xFF813F7D062AC0BC] = "AA0B5C77F088CCC2D39049BD267F066D".FromHexString(), // TactKeyId 25
            [0xD1E9B5EDF9283668] = "8E4A2579894E38B4AB9058BA5C7328EE".FromHexString(), // TactKeyId 39
            [0xB76729641141CB34] = "9849D1AA7B1FD09819C5C66283A326EC".FromHexString(), // TactKeyId 40
            [0xFFB9469FF16E6BF8] = "D514BD1909A9E5DC8703F4B8BB1DFD9A".FromHexString(), // TactKeyId 41
            [0x23C5B5DF837A226C] = "1406E2D873B6FC99217A180881DA8D62".FromHexString(), // TactKeyId 42
            //[0x3AE403EF40AC3037] = "????????????????????????????????".FromHexString(), // TactKeyId 51
            [0xE2854509C471C554] = "433265F0CDEB2F4E65C0EE7008714D9E".FromHexString(), // TactKeyId 52
            [0x8EE2CB82178C995A] = "DA6AFC989ED6CAD279885992C037A8EE".FromHexString(), // TactKeyId 55
            [0x5813810F4EC9B005] = "01BE8B43142DD99A9E690FAD288B6082".FromHexString(), // TactKeyId 56
            [0x7F9E217166ED43EA] = "05FC927B9F4F5B05568142912A052B0F".FromHexString(), // TactKeyId 57
            [0xC4A8D364D23793F7] = "D1AC20FD14957FABC27196E9F6E7024A".FromHexString(), // TactKeyId 58
            [0x40A234AEBCF2C6E5] = "C6C5F6C7F735D7D94C87267FA4994D45".FromHexString(), // TactKeyId 59
            [0x9CF7DFCFCBCE4AE5] = "72A97A24A998E3A5500F3871F37628C0".FromHexString(), // TactKeyId 60
            [0x4E4BDECAB8485B4F] = "3832D7C42AAC9268F00BE7B6B48EC9AF".FromHexString(), // TactKeyId 61
            [0x94A50AC54EFF70E4] = "C2501A72654B96F86350C5A927962F7A".FromHexString(), // TactKeyId 62
            [0xBA973B0E01DE1C2C] = "D83BBCB46CC438B17A48E76C4F5654A3".FromHexString(), // TactKeyId 63
            [0x494A6F8E8E108BEF] = "F0FDE1D29B274F6E7DBDB7FF815FE910".FromHexString(), // TactKeyId 64
            [0x918D6DD0C3849002] = "857090D926BB28AEDA4BF028CACC4BA3".FromHexString(), // TactKeyId 65
            [0x0B5F6957915ADDCA] = "4DD0DC82B101C80ABAC0A4D57E67F859".FromHexString(), // TactKeyId 66
            [0x794F25C6CD8AB62B] = "76583BDACD5257A3F73D1598A2CA2D99".FromHexString(), // TactKeyId 67
            [0xA9633A54C1673D21] = "1F8D467F5D6D411F8A548B6329A5087E".FromHexString(), // TactKeyId 68
            [0x5E5D896B3E163DEA] = "8ACE8DB169E2F98AC36AD52C088E77C1".FromHexString(), // TactKeyId 69
            [0x0EBE36B5010DFD7F] = "9A89CC7E3ACB29CF14C60BC13B1E4616".FromHexString(), // TactKeyId 70
            [0x01E828CFFA450C0F] = "972B6E74420EC519E6F9D97D594AA37C".FromHexString(), // TactKeyId 71
            [0x4A7BD170FE18E6AE] = "AB55AE1BF0C7C519AFF028C15610A45B".FromHexString(), // TactKeyId 72
            [0x69549CB975E87C4F] = "7B6FA382E1FAD1465C851E3F4734A1B3".FromHexString(), // TactKeyId 73
            [0x460C92C372B2A166] = "946D5659F2FAF327C0B7EC828B748ADB".FromHexString(), // TactKeyId 74
            [0x8165D801CCA11962] = "CD0C0FFAAD9363EC14DD25ECDD2A5B62".FromHexString(), // TactKeyId 75
            [0xA3F1C999090ADAC9] = "B72FEF4A01488A88FF02280AA07A92BB".FromHexString(), // TactKeyId 81
            //[0x18AFDF5191923610] = "????????????????????????????????".FromHexString(), // TactKeyId 82
            //[0x3C258426058FBD93] = "????????????????????????????????".FromHexString(), // TactKeyId 91
            [0x094E9A0474876B98] = "E533BB6D65727A5832680D620B0BC10B".FromHexString(), // TactKeyId 92
            [0x3DB25CB86A40335E] = "02990B12260C1E9FDD73FE47CBAB7024".FromHexString(), // TactKeyId 93
            [0x0DCD81945F4B4686] = "1B789B87FB3C9238D528997BFAB44186".FromHexString(), // TactKeyId 94
            [0x486A2A3A2803BE89] = "32679EA7B0F99EBF4FA170E847EA439A".FromHexString(), // TactKeyId 95
            [0x71F69446AD848E06] = "E79AEB88B1509F628F38208201741C30".FromHexString(), // TactKeyId 97
            [0x211FCD1265A928E9] = "A736FBF58D587B3972CE154A86AE4540".FromHexString(), // TactKeyId 98
            [0x0ADC9E327E42E98C] = "017B3472C1DEE304FA0B2FF8E53FF7D6".FromHexString(), // TactKeyId 99
            [0xBAE9F621B60174F1] = "38C3FB39B4971760B4B982FE9F095014".FromHexString(), // TactKeyId 100
            [0x34DE1EEADC97115E] = "2E3A53D59A491E5CD173F337F7CD8C61".FromHexString(), // TactKeyId 101
            [0xE07E107F1390A3DF] = "290D27B0E871F8C5B14A14E514D0F0D9".FromHexString(), // TactKeyId 102
            [0x32690BF74DE12530] = "A2556210AE5422E6D61EDAAF122CB637".FromHexString(), // TactKeyId 103
            [0xBF3734B1DCB04696] = "48946123050B00A7EFB1C029EE6CC438".FromHexString(), // TactKeyId 104
            [0x74F4F78002A5A1BE] = "C14EEC8D5AEEF93FA811D450B4E46E91".FromHexString(), // TactKeyId 105
            //[0x423F07656CA27D23] = "????????????????????????????????".FromHexString(), // TactKeyId 107
            //[0x0691678F83E8A75D] = "????????????????????????????????".FromHexString(), // TactKeyId 108
            //[0x324498590F550556] = "????????????????????????????????".FromHexString(), // TactKeyId 109
            //[0xC02C78F40BEF5998] = "????????????????????????????????".FromHexString(), // TactKeyId 110
            //[0x47011412CCAAB541] = "????????????????????????????????".FromHexString(), // TactKeyId 111
            //[0x23B6F5764CE2DDD6] = "????????????????????????????????".FromHexString(), // TactKeyId 112
            //[0x8E00C6F405873583] = "????????????????????????????????".FromHexString(), // TactKeyId 113
            [0x78482170E4CFD4A6] = "768540C20A5B153583AD7F53130C58FE".FromHexString(), // TactKeyId 114
            [0xB1EB52A64BFAF7BF] = "458133AA43949A141632C4F8596DE2B0".FromHexString(), // TactKeyId 115
            [0xFC6F20EE98D208F6] = "57790E48D35500E70DF812594F507BE7".FromHexString(), // TactKeyId 117
            [0x402CFABF2020D9B7] = "67197BCD9D0EF0C4085378FAA69A3264".FromHexString(), // TactKeyId 118
            [0x6FA0420E902B4FBE] = "27B750184E5329C4E4455CBD3E1FD5AB".FromHexString(), // TactKeyId 119
            [0x1076074F2B350A2D] = "88BF0CD0D5BA159AE7CB916AFBE13865".FromHexString(), // TactKeyId 121
            [0x816F00C1322CDF52] = "6F832299A7578957EE86B7F9F15B0188".FromHexString(), // TactKeyId 122
            [0xDDD295C82E60DB3C] = "3429CC5927D1629765974FD9AFAB7580".FromHexString(), // TactKeyId 123
            [0x83E96F07F259F799] = "91F7D0E7A02CDE0DE0BD367FABCB8A6E".FromHexString(), // TactKeyId 124
            [0x49FBFE8A717F03D5] = "C7437770CF153A3135FA6DC5E4C85E65".FromHexString(), // TactKeyId 225
            [0xC1E5D7408A7D4484] = "A7D88E52749FA5459D644523F8359651".FromHexString(), // TactKeyId 226
            [0xE46276EB9E1A9854] = "CCCA36E302F9459B1D60526A31BE77C8".FromHexString(), // TactKeyId 227
            [0xD245B671DD78648C] = "19DCB4D45A658B54351DB7DDC81DE79E".FromHexString(), // TactKeyId 228
            [0x4C596E12D36DDFC3] = "B8731926389499CBD4ADBF5006CA0391".FromHexString(), // TactKeyId 229
            [0x0C9ABD5081C06411] = "25A77CD800197EE6A32DD63F04E115FA".FromHexString(), // TactKeyId 230
            [0x3C6243057F3D9B24] = "58AE3E064210E3EDF9C1259CDE914C5D".FromHexString(), // TactKeyId 231
            [0x7827FBE24427E27D] = "34A432042073CD0B51627068D2E0BD3E".FromHexString(), // TactKeyId 232
            [0xFAF9237E1186CF66] = "AE787840041E9B4198F479714DAD562C".FromHexString(), // TactKeyId 233
            //[0x5DD92EE32BBF9ABD] = "????????????????????????????????".FromHexString(), // TactKeyId 234
            [0x0B68A7AF5F85F7EE] = "27AA011082F5E8BBBD71D1BA04F6ABA4".FromHexString(), // TactKeyId 236
            [0x01531713C83FCC39] = "E788444360C69DA0EA617D1D9A779DB4".FromHexString(), // TactKeyId 237
            [0x76E4F6739A35E8D7] = "05CF276722E7165C5A4F6595256A0BFB".FromHexString(), // TactKeyId 238
            [0x66033F28DC01923C] = "9F9519861490C5A9FFD4D82A6D0067DB".FromHexString(), // TactKeyId 239
            [0xFCF34A9B05AE7E6A] = "E7C2C8F77E30AC240F39EC23971296E5".FromHexString(), // TactKeyId 240
            [0xE2F6BD41298A2AB9] = "C5DC1BB43B8CF3F085D6986826B928EC".FromHexString(), // TactKeyId 241
            [0x14C4257E557B49A1] = "064A9709F42D50CB5F8B94BC1ACFDD5D".FromHexString(), // TactKeyId 242
            [0x1254E65319C6EEFF] = "79D2B3D1CCB015474E7158813864B8E6".FromHexString(), // TactKeyId 243
            [0xC8753773ADF1174C] = "1E0E37D42EE5CE5E8067F0394B0905F2".FromHexString(), // TactKeyId 244
            [0x2170BCAA9FA96E22] = "6DDA6D48D72DC8005DB9DC15368D35BC".FromHexString(), // TactKeyId 245
            [0x75485627AA225F4D] = "8B7FD50CBACF3328B7C4C52051910AA4".FromHexString(), // TactKeyId 246
            [0x08717B15BF3C7955] = "4B06BF9D17663CEB3312EA3C69FBC5DD".FromHexString(), // TactKeyId 248
            [0xD19DCF7ACA8D96D6] = "520421C1070D930C045516D231C9D442".FromHexString(), // TactKeyId 249
            [0x9FD609902B4B2E07] = "ABE0C5F9C123E6E24E7BEA43C2BF00AC".FromHexString(), // TactKeyId 250
            [0xCB26B441FAE4C8CD] = "2AF37F82884CE97BBBA76113D050F853".FromHexString(), // TactKeyId 251
            [0xA98C7594F55C02F0] = "EEDB77473B721DED6204A976C9A661E7".FromHexString(), // TactKeyId 252
            [0x259EE68CD9E76DBA] = "465D784F1019661CCF417FE466801283".FromHexString(), // TactKeyId 253
            [0x6A026290FBDB3754] = "3D2D620850A6765DD591224F605B949A".FromHexString(), // TactKeyId 255
            [0xCF72FD04608D36ED] = "A0A889976D02FA8D00F7AF0017AD721F".FromHexString(), // TactKeyId 257
            [0x17F07C2E3A45DB3D] = "6D3886BDB91E715AE7182D9F3A08F2C9".FromHexString(), // TactKeyId 258
            [0xDFAB5841B87802B5] = "F37E96ED8A1F8D852F075DDE37C71327".FromHexString(), // TactKeyId 259
            [0xC050FA06BB0538F6] = "C552F5D0B72231502D2547314E6015F7".FromHexString(), // TactKeyId 260
            [0xAB5CDD3FC321831F] = "E1384F5B06EBBCD333695AA6FFC68318".FromHexString(), // TactKeyId 261
            [0xA7B7D1F12395040E] = "36AD3B31273F1EBCEE8520AAA74B12F2".FromHexString(), // TactKeyId 262
            [0x83A2AB72DD8AE992] = "023CFF062B19A529B9F14F9B7AAAC5BB".FromHexString(), // TactKeyId 263
            [0xBEAF567CC45362F0] = "8BD3ED792405D9EE742BF6AFA944578A".FromHexString(), // TactKeyId 264
            [0x7BB3A77FD8D14783] = "4C94E3609CFE0A82000A0BD46069AC6F".FromHexString(), // TactKeyId 265
            [0x8F4098E2470FE0C8] = "AA718D1F1A23078D49AD0C606A72F3D5".FromHexString(), // TactKeyId 266
            [0x6AC5C837A2027A6B] = "B0B7CE091763D15E7F69A8E2342CDD7C".FromHexString(), // TactKeyId 267
            [0x302AAD8B1F441D95] = "24B86438CF02538649E5BA672FD5993A".FromHexString(), // TactKeyId 271
            [0x5C909F00088734B9] = "CFA2176F2ECC15F14A97F83B6D307C71".FromHexString(), // TactKeyId 272
            [0xF785977C76DE9C77] = "7F3C1951F5283A18C1C6D45B6867B51A".FromHexString(), // TactKeyId 273
            [0x1CDAF3931871BEC3] = "66B4D34A3AF30E5EB7F414F6C30AAF4F".FromHexString(), // TactKeyId 275
            [0x814E1AB43F3F9345] = "B65E2A63A116AA251FA5D7B0BAABF778".FromHexString(), // TactKeyId 276
            [0x1FBE97A317FFBEFA] = "BD71F78D43117C68724BB6E0D9577E08".FromHexString(), // TactKeyId 277
            [0x30581F81528FB27C] = "72D452EFB993B1301FF58AA89B188F14".FromHexString(), // TactKeyId 278
            //[0x4287F49A5BB366DA] = "????????????????????????????????".FromHexString(), // TactKeyId 279
            [0xD134F430A45C1CF2] = "543DA784D4BD2428CFB5EBFEBA762A90".FromHexString(), // TactKeyId 280
            //[0x01C82EE0725EDA3A] = "????????????????????????????????".FromHexString(), // TactKeyId 281
            //[0x04C0C50B5BE0CC78] = "????????????????????????????????".FromHexString(), // TactKeyId 282
            //[0xA26FD104489B3DE5] = "????????????????????????????????".FromHexString(), // TactKeyId 283
            //[0xEA6C3B8F210A077F] = "????????????????????????????????".FromHexString(), // TactKeyId 284
            [0x4A738212694AD0B6] = "CE82AF83F39A0AF349C85EC6A153B2BF".FromHexString(), // TactKeyId 285
            //[0x2A430C60DDCC75FF] = "????????????????????????????????".FromHexString(), // TactKeyId 286
            [0x0A096FB251CFF471] = "05C75912ECFF040F85FB4697C99C7703".FromHexString(), // TactKeyId 287
            //[0x205AFFCDFBA639CB] = "????????????????????????????????".FromHexString(), // TactKeyId 288
            [0x32B62CF10571971F] = "18B83FDD5E4B397FB89BB5724675CCBA".FromHexString(), // TactKeyId 289
            [0xB408D6CDE8E0D4C1] = "26FF98806A33ADE74EBBCBE51147B79B".FromHexString(), // TactKeyId 290
            [0x1DBE03EF5A0059E1] = "D63B263CB1C7E85623CC425879CC592D".FromHexString(), // TactKeyId 294
            [0x29D08CEA080FDB84] = "065132A6428B19DFCB2B68948BE958F5".FromHexString(), // TactKeyId 295
            [0x3FE91B3FD7F18B37] = "C913B1C20DAEC804E9F8D3527F2A05F7".FromHexString(), // TactKeyId 296
            [0xF7BECC6682B9EF36] = "43C516580D31E945F24BF05BE25F6DC7".FromHexString(), // TactKeyId 297
            [0xDCB5C5DC78520BD6] = "EA639F09F0134D0498E8427B2BF1245B".FromHexString(), // TactKeyId 298
            [0x566DF4A5A9E3341F] = "D7016188DC431ED342C1DF10AC35243B".FromHexString(), // TactKeyId 299
            [0x9183F8AAA603704D] = "EFDD3E80BEEAD6B18644B1ED5EF27154".FromHexString(), // TactKeyId 300
            [0x856D38B447512C51] = "9F8582A30CF56D1296E84E507FE26598".FromHexString(), // TactKeyId 301
            [0x1D0614B43A9D6DF9] = "1847398AE16C0869C8DA6BA284BA6351".FromHexString(), // TactKeyId 302
            [0x19742EF8BC509417] = "96CE130BB554C9C3990F4D288FC268F7".FromHexString(), // TactKeyId 303
            [0x0A88670B2C572700] = "F13D34D5B2A37F634F0A806058009E85".FromHexString(), // TactKeyId 304
            [0xDA2615B5C0237D39] = "83CBFFFF31B953FED8741A2AA633DEAC".FromHexString(), // TactKeyId 306
            //[0xB6FF5BC63B2F8172] = "????????????????????????????????".FromHexString(), // TactKeyId 307
            [0x90E01E041D38A8B0] = "177ACE267F192EF66781F38A6D9CA587".FromHexString(), // TactKeyId 309
            [0x8FD76F6044F9AAB1] = "BE406D7041D1AAF4FB333F8C685F598C".FromHexString(), // TactKeyId 310
            [0x40377D9CE69C6E30] = "BD9A873E2EEC420BE01AF4DD01B06672".FromHexString(), // TactKeyId 311
            [0xFDEE9569100B1D53] = "D74729112732728ED33FA88DF0E8839B".FromHexString(), // TactKeyId 312
            [0x4F68D9D5A1918F0D] = "4B1D03B4F55CE7C065BF0D6EDBCA8954".FromHexString(), // TactKeyId 313
            [0x99882D68AADCFA6D] = "81369F7FCFCDC8FCD8809575C180AE04".FromHexString(), // TactKeyId 314
            [0x02CC0FC116A9C190] = "87549A9C440CC782B3BE065852AEA70E".FromHexString(), // TactKeyId 315
            [0xBC5C79FC6E592D81] = "C70B768131515E382C9084489CB0B2A7".FromHexString(), // TactKeyId 316
            [0xC737DD0E709977BD] = "D69A4B2F4F27044C9F7A3FE5B6131E3B".FromHexString(), // TactKeyId 317
            [0x33C93E43A1846B30] = "AC81CF1E4083302B7BA7D692213B2F65".FromHexString(), // TactKeyId 318
            [0x240745D093CEBD04] = "7E76AEF0BE825D9610DE070A3753C229".FromHexString(), // TactKeyId 319
            //[0x73E8CCF0812E8809] = "????????????????????????????????".FromHexString(), // TactKeyId 320
            [0xED4224DDF3776EB0] = "795B1C3735F7971D5D6373B0FD1976EA".FromHexString(), // TactKeyId 322
            [0x60C7EDA6A7BCDED0] = "8B42DEFA9E04A28841D3824DD095AEB3".FromHexString(), // TactKeyId 323
            //[0x1297977C87A557D5] = "????????????????????????????????".FromHexString(), // TactKeyId 324
            [0x6CD8165A18D613CA] = "CD86A3ED4D8DE1C119B3D5FEB0DC6FE9".FromHexString(), // TactKeyId 325
            [0x3B5D811B6C4B0987] = "8F03F16ACDC46A038BF1AC8C935C9EF9".FromHexString(), // TactKeyId 326
            [0x2513CE4CF5A5DACB] = "54ADA72DF483B81057551C2063A7D543".FromHexString(), // TactKeyId 327
            [0xCE6A8C3E23432875] = "3D264E57D0AA757BDCF6EDE5112F196B".FromHexString(), // TactKeyId 328
            [0x7778A0E0914354FA] = "92DC776A58C39DD3308DCEB494F3A444".FromHexString(), // TactKeyId 329
            [0xC4E751C98189FA5B] = "50434829A36BB20A402696F4FB554B2B".FromHexString(), // TactKeyId 330
            [0xF30F2C1A6FEEF618] = "6F9D752DF8A7C594E90F62FEA68577DE".FromHexString(), // TactKeyId 331
            //[0x784D9A78CAB17AFC] = "????????????????????????????????".FromHexString(), // TactKeyId 332
            //[0xDAED22AE797E4EF1] = "????????????????????????????????".FromHexString(), // TactKeyId 333
            [0x428811AD4C462334] = "AEAD73A70361D56CD5123A5E20BC754A".FromHexString(), // TactKeyId 334
            [0x39044914C846DB5F] = "F7037B324CAC658684944FB287241C24".FromHexString(), // TactKeyId 335
            [0x98D3909D3C2358A9] = "8553AB1E4C4A86052B3D66AA0FA13937".FromHexString(), // TactKeyId 338
            [0x7412D6BD04C6686D] = "F89B4A51AF08ADFBE4CFF8EBEBEDA55A".FromHexString(), // TactKeyId 339
            [0x598F3D6BC8233EA5] = "050E9283C3CE59F4A7BFDABA6689C5D2".FromHexString(), // TactKeyId 340
            [0x943AE6AC59D361D9] = "EA29E5A036D98CD3BC308783D5CE97EB".FromHexString(), // TactKeyId 341
            //[0xFC9637CDB85FD83E] = "????????????????????????????????".FromHexString(), // TactKeyId 342
            [0x47FEFED7D7CE2893] = "6254F2D8ADEEA548BDCCF523C8907D23".FromHexString(), // TactKeyId 343
            //[0xF23FDEAD4B55445F] = "????????????????????????????????".FromHexString(), // TactKeyId 344
            //[0xD735851256FD94F8] = "????????????????????????????????".FromHexString(), // TactKeyId 345
            //[0x0B5583337754BDB4] = "????????????????????????????????".FromHexString(), // TactKeyId 346
            [0xC50DE5DE7388AF03] = "95025DEC854DBA5C3382BBC431227031".FromHexString(), // TactKeyId 347
            [0xBC2FB09C6452F082] = "2863AEE82102C9E0E5BBDF7F3A15B03E".FromHexString(), // TactKeyId 348
            [0x89E1427153BD4A31] = "2D0063A14D6907E8F117EA79C94E9048".FromHexString(), // TactKeyId 349
            [0xBF7B1B1DB3CE52B1] = "731F3626D9627D3349C68995C8CA7303".FromHexString(), // TactKeyId 350
            [0xF04F377D7BD10A5E] = "251876C9F699AA3AF9B68BDE632388D0".FromHexString(), // TactKeyId 351
            [0xD85B0317B72EB7DB] = "85F4E3A42ECFF4A8C610CFFA59B291D7".FromHexString(), // TactKeyId 352
            [0x152155855D497807] = "B1165F52629DFB9FD4006F9F1F55CD97".FromHexString(), // TactKeyId 353
            [0xB29C6246918DDF64] = "3281AC312100C40FE2923268E1F900FB".FromHexString(), // TactKeyId 354
            [0xAD1D49C129D857CA] = "BCDFCF7B1148A601FA3E98972F454C91".FromHexString(), // TactKeyId 355
            //[0x030DDEB1D4FA3FCE] = "????????????????????????????????".FromHexString(), // TactKeyId 356
            //[0xD874F70874B5C5FA] = "????????????????????????????????".FromHexString(), // TactKeyId 357
            [0xA263FE68D73AA3DA] = "4E34A82C7514B0326057D68C3E47ACE3".FromHexString(), // TactKeyId 358
            [0x1E04A8DDB3C1DC0B] = "F04BCC714CBD05379130230F56F6A1AB".FromHexString(), // TactKeyId 359
            [0x512D18B506449AFC] = "A83BBE791A15A4A658E1A02B2CB345C9".FromHexString(), // TactKeyId 360
            [0x4903F480BD987FA9] = "C30D496973BB56735EF4DCF475B76BE8".FromHexString(), // TactKeyId 361
            //[0x83C9EE5007C286B8] = "????????????????????????????????".FromHexString(), // TactKeyId 362
            //[0xDE46E0F9C14E9587] = "????????????????????????????????".FromHexString(), // TactKeyId 363
            [0x01A08F7CDA6E8F17] = "4CFD6698428976499CADF907F1A38B67".FromHexString(), // TactKeyId 364
            [0xB91972925E37456B] = "90E861B92CD403CEB08AAF386696B63B".FromHexString(), // TactKeyId 365
            //[0xB2708293A625EE14] = "????????????????????????????????".FromHexString(), // TactKeyId 366
            //[0x3E95E5966AB6E894] = "????????????????????????????????".FromHexString(), // TactKeyId 367
            //[0xB3A1B4341CFA4F4D] = "????????????????????????????????".FromHexString(), // TactKeyId 368
            //[0x8248F003C1441ADB] = "????????????????????????????????".FromHexString(), // TactKeyId 369
            //[0x48E73BE8D5EAF4B5] = "????????????????????????????????".FromHexString(), // TactKeyId 370
            //[0x8343AEF07B977C79] = "????????????????????????????????".FromHexString(), // TactKeyId 371
            //[0xDA9A6CB91E3D26B0] = "????????????????????????????????".FromHexString(), // TactKeyId 372
            [0x1857CBD1054F94CC] = "F09835F7DC2FFD68F15522FEA7C733BD".FromHexString(), // TactKeyId 373
            [0xE791BF82C9499456] = "E1C5CD6D07D7F1266A80258F4109FA9E".FromHexString(), // TactKeyId 374
            //[0x8E202FAB57DFD542] = "????????????????????????????????".FromHexString(), // TactKeyId 375
            //[0x37800F07285A7BC9] = "????????????????????????????????".FromHexString(), // TactKeyId 376
            //[0x1B8739F92CDAD324] = "????????????????????????????????".FromHexString(), // TactKeyId 377
            //[0x0947962739D1A90A] = "????????????????????????????????".FromHexString(), // TactKeyId 378
            [0xCDD717BF616F1282] = "ACAD2BC932F849C1617DA0D6BA1E6C6D".FromHexString(), // TactKeyId 379
            [0xA8BBCCA0A621E518] = "58886416E7F9A6A9D9CB721229B69496".FromHexString(), // TactKeyId 380
            [0x7D38C6BF343CAB32] = "83A61059E4628FDF22FF68C2555DDCD4".FromHexString(), // TactKeyId 381
            [0xD01FFF442611F3C0] = "9B2D1A34FAB0FBA75DC58F88E8F9A38D".FromHexString(), // TactKeyId 382
            //[0x1E4534FEEC561A02] = "????????????????????????????????".FromHexString(), // TactKeyId 384
            //[0x1917CCB5CF95CD5E] = "????????????????????????????????".FromHexString(), // TactKeyId 385
            //[0x77C4EB395B13E06C] = "????????????????????????????????".FromHexString(), // TactKeyId 386
            [0xB9A5FE4AAF12D195] = "11DC97DED64BF545056473CB8C53A58A".FromHexString(), // TactKeyId 387
            //[0x50F45BF6BC2B387D] = "????????????????????????????????".FromHexString(), // TactKeyId 390
            [0x103C4D80B653E8C5] = "883B853EC85EAABF82DC6D8764957EE6".FromHexString(), // TactKeyId 392
            //[0x7FC49C7CDE481335] = "????????????????????????????????".FromHexString(), // TactKeyId 393
            //[0x11103693800FE621] = "????????????????????????????????".FromHexString(), // TactKeyId 395
            //[0x70AEC09B193DE12B] = "????????????????????????????????".FromHexString(), // TactKeyId 396
            [0xCAC9A9AD68A71FD3] = "1B9017C0DABC23F70E845DFBE99D5F17".FromHexString(), // TactKeyId 397
            //[0x6BBB68F40F470401] = "????????????????????????????????".FromHexString(), // TactKeyId 398
            //[0x7B7CF6626F5CF873] = "????????????????????????????????".FromHexString(), // TactKeyId 399
            //[0x3146CA370908382D] = "????????????????????????????????".FromHexString(), // TactKeyId 401
            [0x27C95702825CEEA9] = "75F90192F6F560E2F58829FCBD4EC59E".FromHexString(), // TactKeyId 403
            //[0x704D91D56DBB8DD9] = "????????????????????????????????".FromHexString(), // TactKeyId 405
            //[0x068889361B441AAC] = "????????????????????????????????".FromHexString(), // TactKeyId 413
            [0x3C11A51D9EDF7946] = "5B70FD640498C46D586652B9C69761A5".FromHexString(), // TactKeyId 414
            // BNA 1.5.0 Alpha
            [0x2C547F26A2613E01] = "37C50C102D4C9E3A5AC069F072B1417D".FromHexString(),
            // Warcraft III: Reforged
            [0x6E4296823E7D561E] = "C0BFA2943AC3E92286E4443EE3560D65".FromHexString(),
            [0xE04D60E31DDEBF63] = "263DB5C402DA8D4D686309CB2E3254D0".FromHexString(),
        };

        public static Salsa20 SalsaInstance { get; } = new Salsa20();

        public static bool HasKey(ulong keyName) => keys.ContainsKey(keyName);

        public static byte[] GetKey(ulong keyName)
        {
            keys.TryGetValue(keyName, out byte[] key);
            return key;
        }

        public static void SetKey(ulong keyName, byte[] key)
        {
            if (keys.TryGetValue(keyName, out byte[] oldKey))
            {
                if (!oldKey.SequenceEqual(key))
                    Logger.WriteLine($"Duplicate key name {keyName:X16} with different key: old key {oldKey.ToHexString()} new key {key.ToHexString()}");
                else
                    Logger.WriteLine($"Duplicate key name {keyName:X16} with key: {key.ToHexString()}");
            }

            keys[keyName] = key;
        }

        public static void LoadKeys(string keyFile = "TactKey.csv")
        {
            if (File.Exists(keyFile))
            {
                using (StreamReader sr = new StreamReader(keyFile))
                {
                    string line;

                    while ((line = sr.ReadLine()) != null)
                    {
                        string[] tokens = line.Split(';');

                        if (tokens.Length != 2)
                            continue;

                        ulong keyName = ulong.Parse(tokens[0], NumberStyles.HexNumber);
                        string keyStr = tokens[1];

                        if (keyStr.Length != 32)
                            continue;

                        SetKey(keyName, keyStr.FromHexString());
                    }
                }
            }
        }
    }
}
