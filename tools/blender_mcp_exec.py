"""Execute a Blender Python script through the official Blender MCP bridge.

The Blender Lab add-on must be listening on its default localhost port before
this helper is invoked.  Keeping the client tiny makes production runs
repeatable from PowerShell while all scene work still travels through MCP.
"""

from __future__ import annotations

import argparse
import asyncio
import json
from pathlib import Path

from mcp import ClientSession, StdioServerParameters
from mcp.client.stdio import stdio_client


MCP_EXECUTABLE = Path(
    r"C:\Users\d.grab\.codex\mcp-servers\blender_mcp-venv\Scripts\blender-mcp.exe"
)


async def execute(script_path: Path) -> None:
    source = script_path.read_text(encoding="utf-8")
    parameters = StdioServerParameters(command=str(MCP_EXECUTABLE), args=[])
    async with stdio_client(parameters) as (read_stream, write_stream):
        async with ClientSession(read_stream, write_stream) as session:
            await session.initialize()
            result = await session.call_tool(
                "execute_blender_code",
                {"code": compile_wrapper(source, script_path)},
            )
            if result.isError:
                raise RuntimeError("Blender MCP execution failed: " + str(result.content))
            for block in result.content:
                response_text = getattr(block, "text", None)
                if not response_text:
                    continue
                print(response_text)
                try:
                    response = json.loads(response_text)
                except json.JSONDecodeError:
                    continue
                if response.get("status") == "error":
                    raise RuntimeError(response.get("message", response_text))


def compile_wrapper(source: str, script_path: Path) -> str:
    path_literal = repr(str(script_path.resolve()))
    source_literal = repr(source)
    return (
        f"_pelag_script_path = {path_literal}\n"
        f"_pelag_script_source = {source_literal}\n"
        "exec(compile(_pelag_script_source, _pelag_script_path, 'exec'), "
        "{'__name__': '__main__', '__file__': _pelag_script_path})"
    )


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("script", type=Path)
    args = parser.parse_args()
    if not args.script.is_file():
        parser.error(f"script does not exist: {args.script}")
    if not MCP_EXECUTABLE.is_file():
        parser.error(f"MCP executable does not exist: {MCP_EXECUTABLE}")
    asyncio.run(execute(args.script))


if __name__ == "__main__":
    main()
