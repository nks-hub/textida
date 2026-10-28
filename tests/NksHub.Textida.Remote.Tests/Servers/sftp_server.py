import argparse, os, socket, sys, threading, base64, hashlib, json
import paramiko
from paramiko import SFTPAttributes, SFTPServerInterface, SFTPHandle, SFTPServer, SFTP_OK, SFTP_NO_SUCH_FILE, SFTP_FAILURE

ap = argparse.ArgumentParser()
ap.add_argument("--root", required=True)
ap.add_argument("--hostkey", required=True)
ap.add_argument("--user", default="u")
ap.add_argument("--password", default="p")
ap.add_argument("--clientkey")          # authorized public key file (OpenSSH format)
ap.add_argument("--port", type=int, default=0)
ap.add_argument("--gen-client-key")      # write a new key pair (PEM and OpenSSH .pub) and authorize it
ap.add_argument("--drop-after", type=int, default=-1)  # kill the connection after this many written bytes
ap.add_argument("--drop-read-after", type=int, default=-1)  # kill the connection after this many bytes were read (a download cut in the middle)
ap.add_argument("--client-key-type", choices=["ed25519", "rsa"], default="ed25519")
# An old server: the SHA-1 signature "ssh-rsa" is the only one it knows, for the login key, for its host key, or both. Current paramiko has dropped it, so it is put back here.
ap.add_argument("--legacy-rsa", choices=["userauth", "hostkey", "both"])
args = ap.parse_args()

if args.legacy_rsa:
    from cryptography.hazmat.primitives import hashes
    paramiko.RSAKey.HASHES["ssh-rsa"] = hashes.SHA1
    paramiko.Transport._key_info["ssh-rsa"] = paramiko.RSAKey
    if args.legacy_rsa in ("userauth", "both"):
        paramiko.Transport._preferred_pubkeys = ("ssh-rsa",)
        # OpenSSH answers a signature algorithm it does not know with a plain refusal, so the client may try the next one; paramiko hangs up instead.
        import paramiko.auth_handler as auth_handler
        class _Refused:
            def get_base64(self):
                return ""
        _generate = auth_handler.AuthHandler._generate_key_from_request
        def _generate_or_refuse(self, algorithm, keyblob):
            if algorithm.replace("-cert-v01@openssh.com", "") not in self.transport.preferred_pubkeys:
                return _Refused()
            return _generate(self, algorithm, keyblob)
        auth_handler.AuthHandler._generate_key_from_request = _generate_or_refuse
    if args.legacy_rsa in ("hostkey", "both"):
        paramiko.Transport._preferred_keys = ("ssh-rsa",)

if os.path.exists(args.hostkey):
    host_key = paramiko.RSAKey(filename=args.hostkey)
else:
    host_key = paramiko.RSAKey.generate(2048)
    host_key.write_private_key_file(args.hostkey)

if args.gen_client_key:
    from cryptography.hazmat.primitives import serialization as ser
    if args.client_key_type == "rsa":
        from cryptography.hazmat.primitives.asymmetric import rsa
        ck = rsa.generate_private_key(public_exponent=65537, key_size=2048)
        pem = ck.private_bytes(ser.Encoding.PEM, ser.PrivateFormat.TraditionalOpenSSL, ser.NoEncryption())
    else:
        from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
        ck = Ed25519PrivateKey.generate()
        pem = ck.private_bytes(ser.Encoding.PEM, ser.PrivateFormat.OpenSSH, ser.NoEncryption())
    open(args.gen_client_key, "wb").write(pem)
    open(args.gen_client_key + ".pub", "wb").write(ck.public_key().public_bytes(ser.Encoding.OpenSSH, ser.PublicFormat.OpenSSH) + b" test\n")
    args.clientkey = args.gen_client_key + ".pub"

root = os.path.abspath(args.root)
written = [0]
served = [0]

class Auth(paramiko.ServerInterface):
    def __init__(self, transport):
        self.transport = transport
    def check_auth_password(self, u, p):
        return paramiko.AUTH_SUCCESSFUL if (u, p) == (args.user, args.password) else paramiko.AUTH_FAILED
    def check_auth_publickey(self, u, key):
        if args.clientkey and u == args.user:
            line = open(args.clientkey).read().split()
            if key.get_base64() == line[1]:
                return paramiko.AUTH_SUCCESSFUL
        return paramiko.AUTH_FAILED
    def get_allowed_auths(self, u):
        return "password,publickey"
    def check_channel_request(self, kind, chanid):
        return paramiko.OPEN_SUCCEEDED if kind == "session" else paramiko.OPEN_FAILED_ADMINISTRATIVELY_PROHIBITED

class Handle(SFTPHandle):
    def __init__(self, transport, flags=0):
        super().__init__(flags)
        self.transport = transport
    def stat(self):
        return SFTPAttributes.from_stat(os.fstat(self.readfile.fileno() if self.readfile else self.writefile.fileno()))
    def chattr(self, attr):
        return SFTP_OK
    def write(self, offset, data):
        written[0] += len(data)
        if args.drop_after >= 0 and written[0] > args.drop_after:
            self.transport.close()
            return SFTP_FAILURE
        return super().write(offset, data)
    def read(self, offset, length):
        data = super().read(offset, length)
        if args.drop_read_after >= 0 and isinstance(data, bytes):
            served[0] += len(data)
            if served[0] > args.drop_read_after:
                self.transport.close()
                return SFTP_FAILURE
        return data

class Files(SFTPServerInterface):
    def __init__(self, server, *a, **k):
        super().__init__(server, *a, **k)
        self.srv = server
    def _p(self, path):
        p = os.path.abspath(os.path.join(root, path.lstrip("/\\")))
        if not (p == root or p.startswith(root + os.sep)):
            raise PermissionError(path)
        return p
    def list_folder(self, path):
        try:
            out = []
            for n in os.listdir(self._p(path)):
                a = SFTPAttributes.from_stat(os.stat(os.path.join(self._p(path), n)))
                a.filename = n
                out.append(a)
            return out
        except OSError as e:
            return SFTPServer.convert_errno(e.errno)
    def stat(self, path):
        try: return SFTPAttributes.from_stat(os.stat(self._p(path)))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
    def lstat(self, path):
        return self.stat(path)
    def open(self, path, flags, attr):
        try:
            p = self._p(path)
            binary_flag = getattr(os, "O_BINARY", 0)
            flags |= binary_flag
            mode = getattr(attr, "st_mode", None) or 0o666
            fd = os.open(p, flags, mode)
        except OSError as e:
            return SFTPServer.convert_errno(e.errno)
        if flags & os.O_WRONLY:
            fstr = "ab" if flags & os.O_APPEND else "wb"
        elif flags & os.O_RDWR:
            fstr = "a+b" if flags & os.O_APPEND else "r+b"
        else:
            fstr = "rb"
        try:
            f = os.fdopen(fd, fstr)
            h = Handle(self.srv.transport, flags)
            h.filename = p
            h.readfile = f
            h.writefile = f
            return h
        except Exception:
            import traceback; traceback.print_exc(); sys.stderr.flush()
            return SFTP_FAILURE
    def remove(self, path):
        try: os.remove(self._p(path))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def rename(self, old, new):
        try:
            if os.path.exists(self._p(new)): return SFTP_FAILURE  # plain SFTP rename never overwrites
            os.rename(self._p(old), self._p(new))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def posix_rename(self, old, new):
        try: os.replace(self._p(old), self._p(new))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def mkdir(self, path, attr):
        try: os.mkdir(self._p(path))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def rmdir(self, path):
        try: os.rmdir(self._p(path))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def chattr(self, path, attr):
        try:
            if attr.st_mtime is not None:
                os.utime(self._p(path), (attr.st_atime or attr.st_mtime, attr.st_mtime))
        except OSError as e: return SFTPServer.convert_errno(e.errno)
        return SFTP_OK
    def canonicalize(self, path):
        p = "/" + os.path.relpath(self._p(path), root).replace("\\", "/")
        return "/" if p == "/." else p

sock = socket.socket()
sock.setsockopt(socket.SOL_SOCKET, socket.SO_REUSEADDR, 1)
sock.bind(("127.0.0.1", args.port))
sock.listen(20)
fp = base64.b64encode(hashlib.sha256(host_key.asbytes()).digest()).decode().rstrip("=")
print(json.dumps({"pid": os.getpid(), "port": sock.getsockname()[1], "sha256": fp}), flush=True)

# The test host keeps the other end of stdin open for as long as it lives. When it is gone (crashed, killed) the read ends and so does this server, which has no end of its own.
def end_with_the_host():
    try:
        sys.stdin.buffer.read()
    finally:
        os._exit(0)

threading.Thread(target=end_with_the_host, daemon=True).start()

def serve(conn):
    t = paramiko.Transport(conn)
    t.add_server_key(host_key)
    t.set_subsystem_handler("sftp", SFTPServer, Files)
    try:
        t.start_server(server=Auth(t))
    except Exception:
        return
    # a Transport thread runs until the client leaves

while True:
    c, _ = sock.accept()
    threading.Thread(target=serve, args=(c,), daemon=True).start()
