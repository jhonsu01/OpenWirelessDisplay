#!/usr/bin/env python3
"""
Gestion de versiones (Capa 3 - Ejecucion deterministica).

Fuente unica de verdad: version.json (raiz del repo).
Propaga la version a:
  - server-windows/Directory.Build.props   (<Version> de .NET, consumido por el .csproj y el MSI)
  - server-windows/installer/version.wxi   (<?define ProductVersion?> para WiX)
  - client-android/app/build.gradle        (versionName + versionCode)
Y registra el cambio en CHANGELOG.md (mueve [Unreleased] a la nueva version).

Uso:
  python execution/bump_version.py patch          # 0.1.0 -> 0.1.1  (+versionCode)
  python execution/bump_version.py minor          # 0.1.0 -> 0.2.0
  python execution/bump_version.py major          # 0.1.0 -> 1.0.0
  python execution/bump_version.py set 1.2.3      # fija una version explicita
  python execution/bump_version.py sync           # solo re-propaga la version actual
  python execution/bump_version.py --check        # imprime version actual y sale

Idempotente: 'sync' puede ejecutarse N veces sin efectos secundarios.
"""
from __future__ import annotations
import json
import re
import sys
from datetime import date
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
VERSION_FILE = ROOT / "version.json"
PROPS_FILE = ROOT / "server-windows" / "Directory.Build.props"
WXI_FILE = ROOT / "server-windows" / "installer" / "version.wxi"
GRADLE_FILE = ROOT / "client-android" / "app" / "build.gradle"
CHANGELOG = ROOT / "CHANGELOG.md"

SEMVER_RE = re.compile(r"^(\d+)\.(\d+)\.(\d+)$")


def load() -> dict:
    return json.loads(VERSION_FILE.read_text(encoding="utf-8"))


def save(data: dict) -> None:
    VERSION_FILE.write_text(json.dumps(data, indent=2, ensure_ascii=False) + "\n", encoding="utf-8")


def parse(v: str) -> tuple[int, int, int]:
    m = SEMVER_RE.match(v)
    if not m:
        raise SystemExit(f"Version invalida (se espera X.Y.Z): {v}")
    return tuple(int(x) for x in m.groups())  # type: ignore


def bump(v: str, part: str) -> str:
    major, minor, patch = parse(v)
    if part == "major":
        return f"{major + 1}.0.0"
    if part == "minor":
        return f"{major}.{minor + 1}.0"
    if part == "patch":
        return f"{major}.{minor}.{patch + 1}"
    raise SystemExit(f"Parte desconocida: {part}")


def write_props(version: str) -> None:
    PROPS_FILE.parent.mkdir(parents=True, exist_ok=True)
    PROPS_FILE.write_text(
        "<Project>\n"
        "  <!-- Generado por execution/bump_version.py. No editar a mano. -->\n"
        "  <PropertyGroup>\n"
        f"    <Version>{version}</Version>\n"
        f"    <AssemblyVersion>{version}.0</AssemblyVersion>\n"
        f"    <FileVersion>{version}.0</FileVersion>\n"
        "  </PropertyGroup>\n"
        "</Project>\n",
        encoding="utf-8",
    )


def write_wxi(version: str) -> None:
    WXI_FILE.parent.mkdir(parents=True, exist_ok=True)
    WXI_FILE.write_text(
        "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
        "<!-- Generado por execution/bump_version.py. No editar a mano. -->\n"
        "<Include>\n"
        f"  <?define ProductVersion=\"{version}\"?>\n"
        "</Include>\n",
        encoding="utf-8",
    )


def write_gradle(version: str, version_code: int) -> None:
    if not GRADLE_FILE.exists():
        return
    text = GRADLE_FILE.read_text(encoding="utf-8")
    text = re.sub(r'versionName\s+"[^"]*"', f'versionName "{version}"', text)
    text = re.sub(r"versionCode\s+\d+", f"versionCode {version_code}", text)
    GRADLE_FILE.write_text(text, encoding="utf-8")


def update_changelog(version: str) -> None:
    if not CHANGELOG.exists():
        return
    text = CHANGELOG.read_text(encoding="utf-8")
    if f"## [{version}]" in text:
        return  # ya registrado
    today = date.today().isoformat()
    text = text.replace(
        "## [Unreleased]",
        f"## [Unreleased]\n\n## [{version}] - {today}",
        1,
    )
    CHANGELOG.write_text(text, encoding="utf-8")


def propagate(data: dict) -> None:
    version = data["version"]
    version_code = int(data["versionCode"])
    write_props(version)
    write_wxi(version)
    write_gradle(version, version_code)
    data["components"]["server-windows"] = version
    data["components"]["client-android"] = version
    print(f"  -> Directory.Build.props  : <Version>{version}</Version>")
    print(f"  -> installer/version.wxi  : ProductVersion={version}")
    print(f"  -> app/build.gradle       : versionName={version}, versionCode={version_code}")


def main(argv: list[str]) -> int:
    if not VERSION_FILE.exists():
        raise SystemExit("No existe version.json en la raiz del repo.")
    data = load()

    if not argv or argv[0] in ("--check", "check"):
        print(f"Version actual: {data['version']} (versionCode {data['versionCode']}, canal {data.get('channel','-')})")
        return 0

    cmd = argv[0]
    if cmd in ("major", "minor", "patch"):
        new_version = bump(data["version"], cmd)
        data["version"] = new_version
        data["versionCode"] = int(data["versionCode"]) + 1
    elif cmd == "set":
        if len(argv) < 2:
            raise SystemExit("Uso: bump_version.py set X.Y.Z")
        parse(argv[1])
        data["version"] = argv[1]
        data["versionCode"] = int(data["versionCode"]) + 1
    elif cmd == "sync":
        pass
    else:
        raise SystemExit(__doc__)

    data["lastBump"] = date.today().isoformat()
    print(f"Version: {data['version']} (versionCode {data['versionCode']})")
    propagate(data)
    update_changelog(data["version"])
    save(data)
    print("OK. Recuerda: edita la seccion [Unreleased] del CHANGELOG antes de publicar.")
    return 0


if __name__ == "__main__":
    raise SystemExit(main(sys.argv[1:]))
