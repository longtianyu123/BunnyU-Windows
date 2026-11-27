using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

if(args.Length<2)throw new ArgumentException("generate key-path | sign key-path manifest-path");
using var key=ECDsa.Create(ECCurve.NamedCurves.nistP256);
if(args[0]=="generate")
{
    if(File.Exists(args[1]))throw new IOException("Signing key already exists.");
    Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(args[1]))!);
    File.WriteAllText(args[1],key.ExportPkcs8PrivateKeyPem());Console.WriteLine(Convert.ToBase64String(key.ExportSubjectPublicKeyInfo()));return;
}
key.ImportFromPem(File.ReadAllText(args[1]));
var manifest=JsonNode.Parse(File.ReadAllText(args[2]))!.AsObject();
var payload=string.Join("\n",manifest["version"]!.GetValue<string>(),manifest["installerUrl"]!.GetValue<string>(),manifest["sha256"]!.GetValue<string>().ToLowerInvariant(),manifest["notes"]?.GetValue<string>()??"");
manifest["signature"]=Convert.ToBase64String(key.SignData(Encoding.UTF8.GetBytes(payload),HashAlgorithmName.SHA256,DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
File.WriteAllText(args[2],manifest.ToJsonString(new JsonSerializerOptions{WriteIndented=true}));
Console.WriteLine("Manifest signed.");
