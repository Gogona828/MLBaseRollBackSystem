#!/bin/zsh
set -eu
cd "${0:A:h}"
print 'Mac LAN IPv4 addresses:'
/sbin/ifconfig | /usr/bin/awk '/inet / && $2 != "127.0.0.1" {print $2}'
exec python3 server.py "$@"
