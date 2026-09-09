using System.Security.Cryptography;

var storedHash = "100000.3zTotfzpgS9AlINx6+jCTA==.XCIARXs071zLaOMQF+He4B45zHePChlNpuG6o9m7AIk=";
var password   = "Admin@Zenride2025!";

var parts        = storedHash.Split('.');
var iterations   = int.Parse(parts[0]);
var salt         = Convert.FromBase64String(parts[1]);
var expectedHash = Convert.FromBase64String(parts[2]);
var actualHash   = Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA512, expectedHash.Length);
var match        = CryptographicOperations.FixedTimeEquals(actualHash, expectedHash);

Console.WriteLine($"Password '{password}' matches hash: {match}");
