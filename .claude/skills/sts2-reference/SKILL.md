---
name: sts2-reference
description: Look up STS2 game data (enemies, encounters, ancients, events, mechanics) from the community reference spreadsheet
user_invocable: true
---

Fetch and parse the STS2 community reference spreadsheet to answer questions about game data.

**Spreadsheet ID:** `1wlhrYjH8JwfPrPd0VaekzJrdKuOXCT23UwxHcQBayVc`

Use the `mcp__gdrive__gdrive_get_file_content` tool with `file_id: 1wlhrYjH8JwfPrPd0VaekzJrdKuOXCT23UwxHcQBayVc` to fetch the spreadsheet content.

The spreadsheet contains these sheets/sections:
- **Act 1: Overgrowth** / **Act 1: Underdocks** — encounter lists
- **Act 2: Hive** — encounter lists
- **Act 3: Glory** — encounter lists
- **Regular monsters** — enemy stats and attack patterns
- **Elites** — elite enemy details
- **Bosses** — boss fight details
- **Ancients** — ancient choices and their options
- **Events (work in progress)** — event details
- **Mechanics/Statistics** — hidden mechanics (rare card chance, potion drop chance, etc.)

**How to use:**
1. Fetch the full spreadsheet content
2. Parse the CSV-like output to find the relevant section
3. Answer the user's question with the specific data

**Common use cases:**
- "What are the attack patterns for [enemy]?"
- "What encounters appear in Act 2?"
- "What are the ancient choice options?"
- "What's the rare card chance at each ascension?"
- "What are the elite stats?"

**Notes:**
- Red values in parentheses in the spreadsheet are ascension-modified values
- Data is from game version v0.100.0 (may update)
- Contributed by dr0gulus, Aplet123, Redbeardy_Mcgee
