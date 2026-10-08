import { execFileSync } from "node:child_process";
import { readFile, writeFile } from "node:fs/promises";
import { join } from "node:path";

export async function createTestCertificates(directory) {
  const ca = join(directory, "ca.pem"), caKey = join(directory, "ca-key.pem");
  const key = join(directory, "server-key.pem"), csr = join(directory, "server.csr"), cert = join(directory, "server.pem");
  const extensions = join(directory, "server.ext");
  const run = args => execFileSync("openssl", args, { stdio: "ignore" });
  run(["req", "-x509", "-newkey", "rsa:2048", "-nodes", "-sha256", "-days", "1", "-subj", "/CN=MedResearch isolated test CA",
    "-addext", "basicConstraints=critical,CA:TRUE", "-addext", "keyUsage=critical,keyCertSign,cRLSign", "-keyout", caKey, "-out", ca]);
  run(["req", "-new", "-newkey", "rsa:2048", "-nodes", "-sha256", "-subj", "/CN=localhost", "-keyout", key, "-out", csr]);
  await writeFile(extensions, "basicConstraints=critical,CA:FALSE\nkeyUsage=critical,digitalSignature,keyEncipherment\nextendedKeyUsage=serverAuth\nsubjectAltName=DNS:localhost,IP:127.0.0.1\n");
  run(["x509", "-req", "-in", csr, "-CA", ca, "-CAkey", caKey, "-CAcreateserial", "-out", cert, "-days", "1", "-sha256", "-extfile", extensions]);
  run(["verify", "-CAfile", ca, "-purpose", "sslserver", "-verify_hostname", "localhost", cert]);
  run(["verify", "-CAfile", ca, "-purpose", "sslserver", "-verify_ip", "127.0.0.1", cert]);
  return { cert: await readFile(cert), private: await readFile(key) };
}
