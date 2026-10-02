"""Isolated temporary Blender MCP worker for the Pelag demo pass.

No user preferences, existing scene or game assets are changed.
"""
import sys

sys.path.insert(0, r"C:\Users\d.grab\.codex\mcp-servers\blender_mcp-src\addon")
from blender_mcp_addon import mcp_to_blender_server, execute_blocking

mcp_to_blender_server.start("localhost", 9876)
print("PELAG_DEMO_MCP_READY localhost:9876 (isolated background worker)", flush=True)
execute_blocking.run()
