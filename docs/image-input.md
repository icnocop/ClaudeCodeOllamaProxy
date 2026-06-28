# Image input

Best-effort: images Copilot sends (data: URLs / base64) are written to temp files and referenced by
path so Claude's `Read` tool can view them; `http(s)` image URLs are passed through as references.
