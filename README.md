# HS Message

Read your Hearthstone whispers whenever you like, instead of catching them the
moment they are spoken.

Built for Battlegrounds, where a message arriving mid combat is currently
impossible to read. HS Message keeps every whisper you receive, files it by
sender, and puts the whole lot on an Alt key layer. It also lets you reply
without opening anything.

It sits on top of [Hearthstone Access](https://hearthstoneaccess.com) and needs
it to be installed.

## Install

Open PowerShell and paste this in:

```powershell
irm https://raw.githubusercontent.com/CodedByGoose/HS-Message/main/install-web.ps1 | iex
```

That is the whole thing. It finds Hearthstone, installs BepInEx if you do not
already have it, downloads the latest release, and puts it in place.

If it says it cannot write to the Hearthstone folder, reopen PowerShell as
administrator and run it again. To do that: press the Windows key, type
`powershell`, then press Shift+Ctrl+Enter.

Then start Hearthstone and press **Alt+H** to hear the command list.

Nothing is installed system wide. Everything lives inside your Hearthstone
folder.

## The problem it solves

Hearthstone Access speaks an incoming whisper once and never stores it. Worse,
its speech manager calls `InterruptTexts()` whenever any key goes down, and that
drains the whole pending speech queue rather than just the current sentence. So
a message that arrives while you are working the tavern gets cut off mid word
and thrown away.

NVDA's speech history is not a workaround, because its review position jumps
back to the newest item every time NVDA speaks. During combat, that is
constantly.

## Commands

Everything is on the Alt layer. Alt is used because Hearthstone Access never
checks for it, so no existing command changes behaviour.

Reading messages from the person you are currently on:

- **Alt+1** through **Alt+9**, and **Alt+0** for the tenth, read the last ten
  messages received, counting back from the newest. Alt+1 is the most recent
  thing said to you. Messages you sent are skipped when counting, so your own
  replies do not use up the ten slots.
- **Alt+Up** and **Alt+Down** step to older and newer messages. These do include
  your own replies, and carry on from wherever a number key left you.
- **Alt+Home** and **Alt+End**, first and last message.
- **Alt+R**, read the whole conversation from the top.

Replying:

- **Alt+M**, write a reply to the person you are currently on. Type, then press
  Enter to send. **F2** reads back what you have typed so far, **Backspace**
  deletes and speaks the character it removed, **Escape** cancels.

Moving between people:

- **Alt+Left** and **Alt+Right**, previous and next person.
- **Alt+Shift+1** through **Alt+Shift+9**, jump straight to a person. Slots are
  handed out in the order people first message you and never shuffle, so
  Alt+Shift+3 keeps meaning the same person all session.
- **Alt+L**, list the people and who has unread messages.

Getting your bearings:

- **Alt+S**, how many conversations and what is unread.
- **Alt+Space**, jump straight to the newest unread message anywhere.
- **Alt+T**, when the current message arrived.

Other:

- **Alt+C**, copy the current message to the clipboard.
- **Alt+B**, send the current message to a braille display without speaking it.
- **Alt+Backspace**, stop talking.
- **Alt+H**, the command list.

Reviewing everything the game has said, not just chat:

- **Alt+comma** and **Alt+period**, move back and forward through the last few
  hundred lines Hearthstone Access has spoken.
- **Alt+slash**, repeat the current line.

That transcript deliberately does not move your review position when new speech
arrives. It is the piece NVDA's speech history gets wrong for this use case.

## Messages are still spoken normally

HS Message does not change how a message is announced when it arrives.
Hearthstone Access speaks it exactly as it does today, and the plugin only keeps
a copy.

## About replying

Hearthstone Access never implemented chat input. `ChatMgr.HandleGUIInput`
returns immediately with the comment "Chat is not implemented yet", so the usual
route is to open the social menu with F4, find the person, and type into a field
no screen reader can see, because there is no real edit control there to read.

Alt+M skips all of that. It runs a small line editor of its own and speaks the
feedback itself, because nothing else will. While it is open, Hearthstone Access
is told to stand down using its own `AllowTextInput` mechanism, the same one it
uses for deck code entry, so no keystroke leaks through to the game.

Two limits worth knowing:

- You can only reply to people already in the buffer. The plugin learns who
  someone actually is from the whisper itself, so it cannot start a brand new
  conversation with somebody who has not messaged you.
- Typing feedback is spoken by the plugin, not by your screen reader's own edit
  box handling, so it does not follow your NVDA keyboard echo settings. Use
  `EchoTypedCharacters` and `EchoTypedWords` in the config to set it.

## Configuration

After the first run, settings live in
`BepInEx\config\com.codedbygoose.hsmessage.cfg`.

- `MaxMessagesPerConversation`, default 200
- `LogToDisk`, default true. Appends every whisper to a dated text file under
  `BepInEx\chat-logs`, so nothing is ever lost even if you close the client.
- `LogDirectory`, blank means the default above
- `BrailleIncomingMessages`, default false. Silently pushes incoming whispers to
  a braille display as they arrive.
- `CaptureAllSpeech`, default true. Powers the transcript keys.
- `TranscriptSize`, default 300 lines
- `EchoTypedCharacters`, default false. Speak every character as you type a
  reply. Accurate but chatty.
- `EchoTypedWords`, default true. Speak each word as you finish it with a space.

## Known limitations

- **Braille is untested.** The braille paths are written against the documented
  Tolk API but have never been exercised against real hardware, because the
  author does not have a display. They are built to fail quietly rather than
  break anything else. Feedback very welcome.
- You cannot start a new conversation, only reply to people who have messaged
  you this session.
- Tested on Windows against Hearthstone 36.4 and Unity 6. Other versions are
  likely fine, since nothing is version specific, but they are unproven.

## Uninstalling

Delete `BepInEx\plugins\HSMessage.dll` from your Hearthstone folder.

To remove BepInEx as well and go back to stock, also delete `winhttp.dll`,
`doorstop_config.ini` and the `BepInEx` folder.

If Hearthstone ever refuses to launch after a game update, delete `winhttp.dll`.
That disables BepInEx and the game returns to normal immediately.

## A word on risk

Hearthstone Access is approved by Blizzard. BepInEx is not. HS Message reads
chat text, keeps it in memory, and optionally writes it to a local file. It
sends nothing anywhere, touches no network, and gives no gameplay advantage. It
is still a third party modification of the client, and that carries some risk to
your account. Decide accordingly.

## Not affiliated with Hearthstone Access

This is a separate downstream project. It contains no Hearthstone Access code
and is not endorsed by that project or by Blizzard. Please do not report
problems with it to the Hearthstone Access developers. Open an issue here
instead.

## How it works

Three hooks, all resolved by name at runtime, so the plugin does not need
rebuilding every time Hearthstone or Hearthstone Access updates:

- A postfix on `ChatBubbleFrame.ReadMessage`, the exact moment a whisper is
  announced. The sender and text are read from the instance, so there is no
  string parsing and no dependence on your display language.
- A prefix on `AccessibilityMgr.HandleKeyboardInput`, the single entry point for
  all of Hearthstone Access's key handling. It is skipped on frames where Alt is
  held, which is what stops Alt+Left from also moving the item cursor.
- An optional postfix on `ScreenReader.Output`, the funnel every spoken line
  passes through, for the transcript.

Review speech goes straight to Tolk rather than through Hearthstone Access's
speech queue. That is deliberate: anything in that queue is destroyed by the
next keypress, including the keypress that asked for it.

If any hook cannot find its target it logs a clear error and is skipped. The
game keeps working and you lose only that feature.

## Building from source

You need the .NET SDK 8 or later. Administrator rights are not required to
build, only to install.

```powershell
.\build.ps1
.\install.ps1
```

`build.ps1` fetches BepInEx into a local `lib\` folder to compile against and
reads the Unity assemblies from your Hearthstone install. Pass
`-HearthstoneDir` to both scripts if the game lives somewhere unusual.

Use `.\build.ps1 -Deploy` to rebuild and copy into the game in one step.

---

CodedByGoose, with the help of Quill (Claude agent)
