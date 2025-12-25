-- NyoCoder --

-- Description -- 

An extension for Visual Studio 2010 that enabled AI-assisted coding with OpenAI-compatible endpoints (llama.cpp, LM Studio, etc.)

-- Configuration (Options > NyoCoder Options...) --

API Key - If your endpoint requires an API key, enter it here.
LLM Server (http://ip:port) - The URL for your AI server.
Model - A model can be specified if supported by your endpoint.
Max Read Lines - Max number of lines that the AI can read from a file at a time
Context Window Size - If known, can be set here to enable automatic context summarization when context fills.

-- Usage --

The extension can be triggered in a text editor either via the right-click menu with "Ask NyoCoder" or via the keybind Ctrl+Alt+N.

-- Tools --

copy_file — Copy a file from one location to another.
delete_file — Delete a file from the file system.
grep_search — Recursively search for a regex pattern in files. (Relies on grep.exe in the extensions directory, included with the release but not with source)
list_directory — List all files and subdirectories in a directory.
move_file — Move or rename a file.
read_file — Read the contents of a local file and return it as a string.
run_shell_command — Execute a shell command on the host system and return its output.
search_replace — Make targeted changes to files using SEARCH/REPLACE blocks.
write_file — Write content to a local file.

-- Credits --

Tool definitions are loosely inspired by Mistral's Vibe CLI.

-- Licensing --
This project's code is licensed under the MIT license.

The VSIX package includes a GPLv3-licensed executable (grep) which is extracted on install; this EXE can be removed if no GPL components are desired, however the grep_search function will no longer work.
