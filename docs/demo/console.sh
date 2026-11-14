#!/usr/bin/env bash
# Starts AiLab.Console for the demo recording, from the repo root.
# - Drops two startup lines that show local paths ("Using launch settings from ..." and "Logs: <folder>",
#   which includes the home folder).
# - Replaces email addresses with "<email hidden>": the agent can quote the tenant admin's address from
#   the audit log. A word with no whitespace after it yet is held back until the next chunk, so an
#   address split across two stream chunks is still caught.
# Everything else passes through unbuffered, so streaming and the "You: " prompt look as without the filter.
# Provider and model come from the environment (the tapes set Chat__Provider / Chat__Model).
set -euo pipefail
cd "$(dirname "$0")/../.."
dotnet run --no-build --project AiLab.Console | perl -e '
  $| = 1; my $head = ""; my $done = 0; my $pending = "";
  sub out { my $s = shift; $s =~ s/[A-Za-z0-9._%+-]+@[A-Za-z0-9.-]+\.[A-Za-z]{2,}/<email hidden>/g; print $s; }
  while (sysread(STDIN, my $buf, 4096)) {
    unless ($done) {
      $head .= $buf;
      next unless $head =~ /Logs: [^\n]*\n/;
      $head =~ s/Using launch settings[^\n]*\n//;
      $head =~ s/Logs: [^\n]*\n//;
      $buf = $head; $done = 1;
    }
    $pending .= $buf;
    if ($pending =~ /\A(.*\s)(\S*)\z/s) { out($1); $pending = $2; }
  }
  out($pending);'
