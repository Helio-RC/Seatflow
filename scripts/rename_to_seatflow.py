#!/usr/bin/env python3
"""批量将项目中的 A_Pair 替换为 SeatFlow。

用法:
  python3 scripts/rename_to_seatflow.py          # 执行替换
  python3 scripts/rename_to_seatflow.py --dry-run  # 仅预览，不写入

覆盖范围: .slnx, .csproj, .cs, .axaml, app.manifest, .json (manifest/about),
          Resources.Designer.cs
排除范围: .resx, .md, scripts/, .git/, bin/, obj/, .claude/, publish/, .version-backups/
"""

import os
import sys
import argparse

PROJECT_ROOT = os.path.dirname(os.path.dirname(os.path.abspath(__file__)))

# 需要处理的文件扩展名
EXTENSIONS = {'.slnx', '.csproj', '.cs', '.axaml', '.manifest', '.json'}

# 排除的目录（相对于项目根目录）
EXCLUDE_DIRS = {
    '.git', 'bin', 'obj', '.claude', 'publish', '.version-backups',
    'scripts',  # 脚本在阶段 4 单独处理
}

# 排除的文件（相对于项目根目录的相对路径后缀匹配）
EXCLUDE_FILE_ENDS = [
    # .resx 在阶段 3 单独处理（需要区分中英文）
    'Resources.resx',
    'Resources.en-US.resx',
    # .md 在阶段 5 单独处理
]

# 特殊文件：需要排除特定替换
# about.json 中的 GitHub URL 暂时保留
GITHUB_URL_PLACEHOLDER = '___GITHUB_URL_PLACEHOLDER___'


def should_process(filepath: str) -> bool:
    """判断文件是否需要处理"""
    rel = os.path.relpath(filepath, PROJECT_ROOT)

    # 检查扩展名
    _, ext = os.path.splitext(filepath)
    if ext not in EXTENSIONS and os.path.basename(filepath) != 'app.manifest':
        return False

    # 检查排除的目录
    parts = rel.replace('\\', '/').split('/')
    for part in parts:
        if part in EXCLUDE_DIRS:
            return False

    # 检查排除的文件
    for end in EXCLUDE_FILE_ENDS:
        if rel.replace('\\', '/').endswith(end):
            return False

    return True


def replace_in_file(filepath: str, dry_run: bool = False) -> int:
    """对单个文件执行替换，返回替换次数"""
    with open(filepath, 'r', encoding='utf-8') as f:
        content = f.read()

    original = content
    is_about_json = filepath.replace('\\', '/').endswith('Data/about.json')

    if is_about_json:
        # about.json: 保护 GitHub URL 不被替换
        content = content.replace(
            'github.com/Helio-RC/A_Pair',
            GITHUB_URL_PLACEHOLDER
        )

    # 执行所有替换
    content = content.replace('A_Pair', 'SeatFlow')

    if is_about_json:
        # 恢复 GitHub URL
        content = content.replace(
            GITHUB_URL_PLACEHOLDER,
            'github.com/Helio-RC/A_Pair'
        )

    count = 0
    if content != original:
        count = 1  # 至少有一处替换
        if not dry_run:
            with open(filepath, 'w', encoding='utf-8') as f:
                f.write(content)

    return count


def main():
    parser = argparse.ArgumentParser(
        description='批量替换 A_Pair → SeatFlow')
    parser.add_argument('--dry-run', action='store_true',
                        help='仅预览，不实际写入')
    args = parser.parse_args()

    files_processed = 0
    files_changed = 0

    for root, dirs, children in os.walk(PROJECT_ROOT):
        # 原地过滤排除目录
        dirs[:] = [d for d in dirs if d not in EXCLUDE_DIRS]

        for fname in children:
            fpath = os.path.join(root, fname)
            if should_process(fpath):
                files_processed += 1
                try:
                    changed = replace_in_file(fpath, dry_run=args.dry_run)
                    if changed:
                        files_changed += 1
                        rel = os.path.relpath(fpath, PROJECT_ROOT)
                        print(f"  {'[DRY RUN] ' if args.dry_run else ''}{rel}")
                except Exception as e:
                    print(f"  ERROR: {os.path.relpath(fpath, PROJECT_ROOT)}: {e}",
                          file=sys.stderr)

    action = "Would modify" if args.dry_run else "Modified"
    print(f"\n{action} {files_changed} of {files_processed} files.")
    if args.dry_run:
        print("Run without --dry-run to apply changes.")


if __name__ == '__main__':
    main()
