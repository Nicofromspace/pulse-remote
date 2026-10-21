using System.Buffers.Binary;
using System.Text;

namespace Pulse.Remote;

internal static class SelfTest
{
    public static void Run(string output)
    {
        var passed = new List<string>();
        void Test(string name, Action test) { test(); passed.Add("PASS " + name); }
        var b = new byte[624];
        void U(int p, uint v) => BinaryPrimitives.WriteUInt32LittleEndian(b.AsSpan(p, 4), v);
        void D(int p, double v) => BitConverter.GetBytes(v).CopyTo(b, p);
        void S(int p, string v) => Encoding.Latin1.GetBytes(v).CopyTo(b, p);
        U(0,0x53695748); U(4,2); U(20,44); U(24,264); U(28,1); U(32,308); U(36,316); U(40,1);
        U(44,123); U(48,1); S(52,"CPU"); U(308,1); U(316,456); S(320,"CPU temperature"); S(576,"°C");
        D(592,52.5); D(600,40); D(608,65); D(616,50);
        void Assert(bool ok) { if(!ok)throw new Exception("Assertion failed"); }
        void Invalid(byte[] data) { try { SensorParser.Parse(data); } catch (InvalidDataException) { return; } throw new Exception("Malformed memory was accepted."); }
        Test("Parses packed values and units",()=>{var r=SensorParser.Parse(b).Readings.Single();Assert(r.Value==52.5&&r.Minimum==40&&r.Maximum==65&&r.Average==50&&r.Unit=="°C"&&r.Group=="CPU");});
        Test("Stable identity",()=>Assert(SensorParser.Parse(b).Readings[0].Id=="0000007B-00000001-000001C8"));
        Test("Zero is a real reading",()=>{D(592,0);Assert(SensorParser.Parse(b).Readings[0].Value==0);});
        Test("Non-finite values become unavailable",()=>{D(592,double.NaN);Assert(SensorParser.Parse(b).Readings[0].Value==null);});
        Test("Rejects truncated header",()=>Invalid(new byte[12]));
        Test("Rejects out-of-bounds section",()=>{U(40,uint.MaxValue);Invalid(b);U(40,1);});
        Test("Rejects bad group index",()=>{U(312,9);Invalid(b);U(312,0);});
        Test("Rejects dead sharing signature",()=>{U(0,0x44454144);Invalid(b);});
        Test("Permanent host accepts only this installation",()=>
        {
            var previous = Program.Settings;
            try
            {
                Program.Settings = new CompanionSettings { HostName = "pulse-12345678.local" };
                Assert(Program.AllowedHost("pulse-12345678.local") && Program.AllowedHost("PULSE-12345678.LOCAL."));
                Assert(Program.AllowedHost("127.0.0.1") && Program.AllowedHost("192.168.4.58"));
                Assert(!Program.AllowedHost("pulse-87654321.local") && !Program.AllowedHost("attacker.example") && !Program.AllowedHost("8.8.8.8"));
            }
            finally { Program.Settings = previous; }
        });
        File.WriteAllLines(output,passed);
    }
}
