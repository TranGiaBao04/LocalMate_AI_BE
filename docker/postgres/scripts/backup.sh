#!/usr/bin/env bash
# Sao lưu database LocalMate (production) ra /opt/backups/localmate, giữ 14 ngày gần nhất.
# Cài trên VPS: copy thành /usr/local/bin/localmate-backup, chmod +x, rồi gọi bằng cron mỗi ngày.
set -euo pipefail

APP_DIR=/opt/localmate
BACKUP_DIR=/opt/backups/localmate
KEEP_DAYS=14

mkdir -p "$BACKUP_DIR"
chmod 700 "$BACKUP_DIR"
target="$BACKUP_DIR/localmate-$(date +%F-%H%M).dump"
tmp="$target.partial"
trap 'rm -f "$tmp"' EXIT

cd "$APP_DIR"
docker compose -f compose.prod.yaml exec -T database \
    sh -c 'pg_dump -Fc -U "$POSTGRES_USER" -d "$POSTGRES_DB"' > "$tmp"

# File hỏng hoặc rỗng thì dừng ở đây, không xoá các bản cũ.
docker compose -f compose.prod.yaml exec -T database pg_restore --list < "$tmp" > /dev/null

mv "$tmp" "$target"
find "$BACKUP_DIR" -name 'localmate-*.dump' -mtime +"$KEEP_DAYS" -delete
echo "$(date '+%F %T') OK $target ($(du -h "$target" | cut -f1))"
