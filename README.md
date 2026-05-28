<div align="center">
  <h1>Mogmail</h1>
  <h3>A better mail service.</h3>
  <p>
    Bulk attachment claiming, bulk delete, inventory pop, and per-character mail archive.
    <br />
    <a href="#installation">Installation</a> · <a href="#features">Features</a> · <a href="#commands">Commands</a> · <a href="#ipc">IPC</a>
  </p>
<p align="center">
    <img src="https://img.shields.io/badge/dynamic/json?url=https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json&query=$[1].DownloadCount&label=Downloads&color=blue&style=for-the-badge" alt="Downloads" />
    <a href="https://ko-fi.com/nexai">
    <img src="https://img.shields.io/badge/Support%20on-Ko--fi-FF5E5B?style=for-the-badge&logo=kofi&logoColor=white" alt="Ko-fi" />
</a>
</p>


</div>

## Installation
1. **Prerequisites**: [FFXIVQuickLauncher](https://github.com/goatcorp/FFXIVQuickLauncher) with Dalamud enabled.
2. **Add Repository**: `/xlsettings` → Experimental → Custom Plugin Repositories:
   ```
   https://raw.githubusercontent.com/Nexaii/dalamud-plugins/main/repo.json
   ```
3. **Install**: `/xlplugins` → Search **Mogmail** → Install.

## Features

### Toolbar

| Button | Left-click | Right-click |
| :--- | :--- | :--- |
| Take | Claim attachments | Claim, then delete the emptied letters |
| Read All | Mark all unread as read | Mark all read, then delete read and empty |
| Delete | Open confirm dialog | (none) |
| Auto Pop | Toggle auto pop after Take | (none) |
| Side | Snap toolbar left or right | (none) |
| Settings | Open settings | (none) |

### Delete
One Delete button. Opens a confirm dialog with a scope picker, a preview of what will be removed, and the last pick remembered. Scopes:

| Scope | Removes |
| :--- | :--- |
| Empty | Letters with no attachments |
| Read and Empty | Letters that are read and have no attachments |
| System | Letters from the Purchases & Rewards (Event Rewards, Mogstation, etc.) |
| All | Every letter (player mail included) |

GM letters are excluded by default. The confirm dialog also gates the Take + Delete right-click on the Take button. Disable in Settings > Confirm before delete (or via the dialog's "Don't ask again" checkbox).

### Pop
Use registrable items (tickets (aetheryte types are ignored), minions, mounts, orchestrions, etc.) straight from inventory. Sensitive categories (Fantasia, MSQ progression, journey items) prompt for confirm. Optional auto pop after every Take.

### Archive
Opt-in per character local record. Headers on receive, body on read. Search, category filter, Markdown export. Keeps 5000 newest. Enable in Settings > General > Archive.

## Commands

| Command | Description |
| :--- | :--- |
| `/mogmail` | Open settings |
| `/mogmail pop` | Start inventory pop (prompts on sensitive items) |
| `/mogmail stop` | Abort all runs and disarm pop |
| `/mogmail archive` | Open archive viewer (no-op message if archive disabled) |

## IPC

Other plugins can drive Mogmail via Dalamud IPC. Mutation calls return `bool`. `true` means the run started, `false` means refused (busy, mailbox closed, not available, or nothing to do). Actions require the mailbox to be open, though Pop does not.

### Readiness

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `Mogmail.IsAvailable` | `bool()` | Logged in and mailbox proxy reachable |
| `Mogmail.IsBusy` | `bool()` | A claim, read, or pop run is in flight |
| `Mogmail.IsMailboxOpen` | `bool()` | Moogle mailbox addon is open |
| `Mogmail.LetterCount` | `int()` | Letters currently loaded |

### Actions

| IPC | Signature | Description |
| :--- | :--- | :--- |
| `Mogmail.ClaimAll` | `bool()` | Claim every letter that has attachments |
| `Mogmail.ClaimAndDelete` | `bool()` | Claim, then delete the emptied letters |
| `Mogmail.ReadAll` | `bool()` | Mark every unread letter as read |
| `Mogmail.ReadAllAndDelete` | `bool()` | Mark read, then delete read and empty |
| `Mogmail.DeleteReadEmpty` | `bool()` | Delete read letters with no attachments |
| `Mogmail.Pop` | `bool(bool allowSensitive)` | Start pop. `true` skips the sensitive confirm |
| `Mogmail.Stop` | `bool()` | Abort runs and disarm pop. `true` if anything stopped |
