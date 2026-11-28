"""One-time setup: a permanent signing key for AutoRejoin Mobile.

Every APK built on GitHub is then signed with the same key, so updates install over the old app and keep its
accounts and settings. Run once (needs Python with `cryptography` and the GitHub CLI signed in):

    python tools/setup-android-signing.py

It writes the key to %USERPROFILE%\\AutoRejoin-signing (keep that folder: without it future versions can't update
existing installs) and stores it in the repository secrets ANDROID_KEYSTORE_B64, ANDROID_KEYSTORE_PASSWORD and
ANDROID_KEY_ALIAS, which .github/workflows/android.yml uses.
"""
import base64
import datetime
import os
import secrets
import subprocess
import sys

from cryptography import x509
from cryptography.hazmat.primitives import hashes, serialization
from cryptography.hazmat.primitives.asymmetric import rsa
from cryptography.hazmat.primitives.serialization import pkcs12
from cryptography.x509.oid import NameOID

REPO = "clxwnlessss/AutoRejoin"
ALIAS = "autorejoin"
out_dir = os.path.join(os.path.expanduser("~"), "AutoRejoin-signing")
key_path = os.path.join(out_dir, "autorejoin-release.p12")
pass_path = os.path.join(out_dir, "password.txt")

if os.path.exists(key_path):
    print(f"A key already exists at {key_path}; using it (delete it first to make a new one).")
    password = open(pass_path, encoding="utf-8").read().strip()
    data = open(key_path, "rb").read()
else:
    os.makedirs(out_dir, exist_ok=True)
    password = secrets.token_urlsafe(24)
    key = rsa.generate_private_key(public_exponent=65537, key_size=2048)
    name = x509.Name([x509.NameAttribute(NameOID.COMMON_NAME, "AutoRejoin Mobile"), x509.NameAttribute(NameOID.ORGANIZATION_NAME, "AutoRejoin")])
    start = datetime.datetime.now(datetime.timezone.utc)
    cert = (x509.CertificateBuilder().subject_name(name).issuer_name(name).public_key(key.public_key())
            .serial_number(x509.random_serial_number()).not_valid_before(start)
            .not_valid_after(start + datetime.timedelta(days=365 * 40)).sign(key, hashes.SHA256()))
    # classic PKCS12 encryption: what Java / apksigner read without trouble
    enc = (serialization.PrivateFormat.PKCS12.encryption_builder().kdf_rounds(50000)
           .key_cert_algorithm(pkcs12.PBES.PBESv1SHA1And3KeyTripleDESCBC).hmac_hash(hashes.SHA1()).build(password.encode()))
    data = pkcs12.serialize_key_and_certificates(ALIAS.encode(), key, cert, None, enc)
    open(key_path, "wb").write(data)
    open(pass_path, "w", encoding="utf-8").write(password)
    print(f"New key: {key_path}")


def secret(name, value):
    r = subprocess.run(["gh", "secret", "set", name, "-R", REPO], input=value.encode(), capture_output=True)
    if r.returncode != 0:
        sys.exit(f"gh secret set {name} failed: {r.stderr.decode().strip()}")
    print(f"  secret {name} set")


secret("ANDROID_KEYSTORE_B64", base64.b64encode(data).decode())
secret("ANDROID_KEYSTORE_PASSWORD", password)
secret("ANDROID_KEY_ALIAS", ALIAS)
print("\nDone. Keep the folder " + out_dir + " safe (a copy on a USB stick or cloud drive is a good idea).")
