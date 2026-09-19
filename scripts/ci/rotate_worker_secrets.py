#!/usr/bin/env python3
"""Worker OSS 密钥同步脚本 — 将 OSS AccessKey 推送到 Cloudflare Worker。

供 worker-secret-sync.yml 定时调用；凭据从环境变量注入（GitHub secrets）。

- 密钥名与 Worker 代码实际读取的绑定一致：OSS_ACCESS_KEY_ID / OSS_ACCESS_KEY_SECRET
- 目标 Worker 支持逗号/空格分隔的多个脚本（共用同一把密钥）：
    CF_WORKER_SCRIPT=oss-proxy,online_worktable

用法:
  CF_ACCOUNT_ID=... CF_API_TOKEN=... CF_WORKER_SCRIPT=oss-proxy,online_worktable \
  OSS_KEY_ID=... OSS_KEY_SECRET=... \
  python3 scripts/ci/rotate_worker_secrets.py
"""

import json
import os
import sys
import urllib.error
import urllib.request

# 与 oss-proxy / online_worktable 两个 Worker 读取的绑定名保持一致
SECRET_ID_NAME = "OSS_ACCESS_KEY_ID"
SECRET_KEY_NAME = "OSS_ACCESS_KEY_SECRET"

REQUIRED = [
    "CF_ACCOUNT_ID",
    "CF_API_TOKEN",
    "OSS_KEY_ID",
    "OSS_KEY_SECRET",
]


def api_base() -> str:
    """兼容两种配置：api.cloudflare.com 或带 /client/v4 的完整基址。"""
    base = (os.environ.get("CF_API_BASE") or "https://api.cloudflare.com").rstrip("/")
    if not base.endswith("/client/v4"):
        base += "/client/v4"
    return base


def worker_scripts() -> list:
    """CF_WORKER_SCRIPT / CF_WORKER_SCRIPTS 支持逗号或空格分隔的多脚本。"""
    raw = os.environ.get("CF_WORKER_SCRIPT") or os.environ.get("CF_WORKER_SCRIPTS") or ""
    return [name for name in raw.replace(",", " ").split() if name]


def push_secrets(account_id: str, script: str, payload: dict) -> bool:
    url = f"{api_base()}/accounts/{account_id}/workers/scripts/{script}/secrets-bulk"
    request = urllib.request.Request(
        url,
        data=json.dumps(payload).encode("utf-8"),
        method="PATCH",
        headers={
            "Authorization": f"Bearer {os.environ['CF_API_TOKEN']}",
            "Content-Type": "application/merge-patch+json",
        },
    )
    try:
        with urllib.request.urlopen(request, timeout=30) as response:
            body = response.read().decode("utf-8")
            print(f"  ✓ {script}（HTTP {response.status}）")
            if body and not json.loads(body).get("success", True):
                print(f"    ✗ 响应异常: {body[:200]}")
                return False
            return True
    except urllib.error.HTTPError as e:
        print(f"  ✗ {script} 返回 HTTP {e.code}")
        print(f"    {e.read().decode('utf-8')[:500]}")
        return False
    except urllib.error.URLError as e:
        print(f"  ✗ {script} 网络错误: {e.reason}")
        return False
    except json.JSONDecodeError:
        print(f"  ✗ {script} 响应不是合法 JSON")
        return False


def main() -> int:
    missing = [key for key in REQUIRED if not os.environ.get(key)]
    if missing:
        print(f"✗ 缺少必需环境变量: {', '.join(missing)}")
        return 1

    scripts = worker_scripts()
    if not scripts:
        print("✗ 缺少必需环境变量: CF_WORKER_SCRIPT（可逗号分隔多个 Worker）")
        return 1

    account_id = os.environ["CF_ACCOUNT_ID"]
    payload = {
        # Cloudflare secrets-bulk 仅支持 PATCH（application/merge-patch+json），
        # 且 body 需包在 "secrets" 下：{"secrets": {"NAME": {"name","text","type"}}}
        "secrets": {
            SECRET_ID_NAME: {
                "name": SECRET_ID_NAME,
                "text": os.environ["OSS_KEY_ID"],
                "type": "secret_text",
            },
            SECRET_KEY_NAME: {
                "name": SECRET_KEY_NAME,
                "text": os.environ["OSS_KEY_SECRET"],
                "type": "secret_text",
            },
        },
    }

    print(f"Worker secrets 轮换：{', '.join(scripts)}")
    failures = [script for script in scripts if not push_secrets(account_id, script, payload)]
    if failures:
        print(f"✗ 以下 Worker 轮换失败: {', '.join(failures)}")
        return 1

    print(f"✓ 已同步 {SECRET_ID_NAME}, {SECRET_KEY_NAME} → {', '.join(scripts)}")
    return 0


if __name__ == "__main__":
    sys.exit(main())
