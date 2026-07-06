# NyoCoder

## Description 

An extension for Visual Studio 2010-2015 that enables AI-assisted coding with OpenAI-compatible endpoints (llama.cpp, LM Studio, etc.)

## Configuration (Options > NyoCoder Options...)

- **General**
  - **API Key** - API key (if required by model provider)
  - **LLM Server** (http(s)://ip:port or http(s)://url) - URL for your AI endpoint.
  - **Model** - Model to use
  - **Max Read Lines** - Max number of lines that the AI can read from a file at a time
  - **Context Window Size** - If known, can be set here to enable automatic context summarization when context fills.
- **Appearance**
  - **Render Markdown in Chat Output** - Renders Markdown formatting in chat (default: enabled)
  - **Show Model Reasoning Output** - Displays model reasoning in chat (default: enabled)
  - **Show Full Tool Execution Output** - Shows full tool output in chat (default: enabled)
- **Indexing**
  - **Mode** - Indexing backend (default: Symbol)
    - **Semantic** - embeddings-based, API required
    - **Symbol** - offline symbol map
    - **Off** - grep only
  - **Embeddings**
    - **Embeddings Endpoint** - OpenAI-compatible embeddings API URL (blank = use **LLM Server**)
    - **Embeddings Model** - Embeddings model name (required for Semantic mode)
    - **Embeddings API Key** - API key for the embeddings endpoint (blank = use **API Key**)
  - **When to Index**
    - **Index on Solution Open** - Automatically index the solution when it is opened (default: enabled)
    - **Re-index Changed Files on Save** - Update the index when files are saved (default: enabled)
- **Tools**
  - **Tools** - List of available tools and their enabled/disabled state
  - **Tools Requiring Approval** - List of tools and whether or not they require approval to run
  - **External Tool Settings** - If SimpleLLMChat tools are installed, their configuration options will be available here.
- **Web Search**
  - **SearXNG Instance**: SearXNG instance to use for running web searches (must support JSON API) (default: none)
  - **User Agent**: User agent to use when making web requests (default: `Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/118.0.5993.90 Safari/537.36`)
  - **Maximum Search Results**: Maximum number of search results to retrieve (default: `20`)
  - **Maximum Web Content Length**: Maximum number of characters to return when reading a webpage (in characters) (default: `8000`)

## Installation

The VSIX for the tool can be downloaded from "Releases". Download and open it to install the extension.

## Usage

The extension can be triggered through its tool window, the right-click menu with "Ask NyoCoder", or via the keybind Ctrl+Alt+N.

## Modes

- **Agent** - AI reads and writes code, and executes commands. (Default)
- **Plan** - AI creates a plan using read-only tools and promptes the user for approval/refinement.
- **Debug** - AI focuses on resolving build errors/bugs.

## Tools

- **ask_user_question** - presents the user with a question and a set of answers, as well as a text box to provide a custom answer
- **copy_file** - copies a file from one location to another
- **delete_file** - deletes a file from the file system
- **codebase_search** - finds code snippets relevant to a natural-language query (uses the configured indexing backend; falls back to grep_search when indexing is off or unavailable)
- **grep_search** - recursively searches for a regex pattern in files (Relies on grep.exe in the extension's directory, included with the release but not with source)
- **list_directory** - lists all files and subdirectories in a directory
- **move_file** - moves or renames a file
- **read_file** - reads the contents of a local file and returns it as a string
- **read_website** - reads the contents of a webpage
- **run_shell_command** - executes a shell command on the host system and return its output
- **run_web_search** - runs a web search (uses SearXNG if available, DuckDuckGo and Wiby if not) (Relies on curl.exe in the extension's directory, included with the release but not with source)
- **manage_plan** - tracks progress on multi-step tasks; supports `read` (view plan) and `write` (write/overwrite plan)
- **search_replace** - makes targeted changes to files using SEARCH/REPLACE blocks
- **write_file** - writes content to a local file

In addition to the listed tools, SimpleLLMChat-compatible tools can be used by extracting them to %appdata%\NyoCoder\Tools.

For more information on these tools, please see [SimpleLLMChat](https://github.com/randomNinja64/SimpleLLMChat) and [SimpleLLMChat-Tool-SDK](https://github.com/randomNinja64/SimpleLLMChat-Tool-SDK).

## Credits

- Tool definitions are loosely inspired by [Mistral's Vibe CLI](https://github.com/mistralai/mistral-vibe)
- cURL builds (for TLS on legacy systems) are provided by [LoRd_MuldeR](https://github.com/lordmulder)

## Licensing
This project's code is licensed under the MIT license.

The VSIX package includes a GPLv3-licensed executable (grep) which is extracted on install; this EXE can be removed if no GPL components are desired, however the grep_search function will no longer work.

Additionally, cURL is bundled as well, licensing information regarding the cURL build is available in the THIRD_PARTY_LICENSES folder in this repo and installed alongside the extension.
