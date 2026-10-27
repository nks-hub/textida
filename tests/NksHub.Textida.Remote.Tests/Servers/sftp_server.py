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
args = ap.parse_args()

if os.path.exists(args.hostkey):
    host_key = paramiko.RSAKey(filename=args.hostkey)
else:
    host_key = paramiko.RSAKey.generate(2048)
    host_key.write_private_key_file(args.hostkey)

if args.gen_client_key:
    # ed25519: SSH.NET offers RSA keys with the SHA-1 signature that current servers (and paramiko) refuse.
    from cryptography.hazmat.primitives.asymmetric.ed25519 import Ed25519PrivateKey
    from cryptography.hazmat.primitives import serialization as ser
    ck = Ed25519PrivateKey.generate()
    open(args.gen_client_key, "wb").write(ck.private_bytes(ser.Encoding.PEM, ser.PrivateFormat.OpenSSH, ser.NoEncryption()))
    open(args.gen_client_key + ".pub", "wb").write(ck.public_key().public_bytes(ser.Encoding.OpenSSH, ser.PublicFormat.OpenSSH) + b" test\n")
    args.clientkey = args.gen_client_key + ".pub"

root = os.path.abspath(args.root)
written = [0]

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
