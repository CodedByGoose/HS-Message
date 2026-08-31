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

Close Hearthstone first, then pick whichever of these suits you.

### Download and run

Download this file and run it:

**https://github.com/CodedByGoose/HS-Message/releases/latest/download/Install-HS-Message.bat**

It lands in your Downloads folder. Press Enter on it to run. Windows may first
ask whether you are sure you want to run a file from the internet: choose Run.
If it needs permission to write to the Hearthstone folder it will ask, and carry
on by itself once you say yes.

**Keep that file.** It is also the updater. It holds no version of its own: every
time you run it, it fetches whatever the newest release is. So to update later,
close Hearthstone and run the same file again. No need to download it afresh.

### One line in PowerShell

If you would rather not download anything, open PowerShell and paste this in:

```powershell
irm https://raw.githubusercontent.com/CodedByGoose/HS-Message/main/install-web.ps1 | iex
```

Or press Windows+R and paste this, which does the same thing without opening
PowerShell yourself:

```
powershell -NoProfile -ExecutionPolicy Bypass -Command "irm https://raw.githubusercontent.com/CodedByGoose/HS-Message/main/install-web.ps1 | iex"
```

### Either way

The installer finds Hearthstone by itself, installs BepInEx if you do not
already have it, downloads the latest release, and puts it in place. It refuses
to run while Hearthstone is open, and checks it can write to the folder before
changing anything.

Then start Hearthstone and press **Alt+H** to hear the command list.

Nothing is installed system wide. Everything lives inside your Hearthstone
folder, and uninstalling is deleting a file.

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
  Enter to send. **Escape** cancels.

Inside that box the usual editing keys all work, and behave the way they do in
any other edit field:

- **Left** and **Right** move a character at a time and speak the character you
  land on. Past the last character reads as "blank", exactly as your screen
  reader would say it.
- **Ctrl+Left** and **Ctrl+Right** move a word at a time and speak the word.
- **Home** and **End** go to the start and the end.
- Hold **Shift** with any of those to select. **Ctrl+A** selects everything.
- **Backspace** and **Delete** remove a character and speak it.
  **Ctrl+Backspace** removes the word behind the caret.
- **Ctrl+V** pastes, **Ctrl+C** copies, **Ctrl+X** cuts. Pasting is the point:
  a link copied from your browser can go straight into a whisper. Line breaks
  in what you paste become spaces, since a whisper is a single line.
- **F2** reads the whole message back. **Shift+F2** says where the caret is.

Punctuation is named as you move over it, so a pasted link reads as "h t t p s
colon slash slash" rather than falling silent on every symbol. The names are
NVDA's own, so nothing new to learn.

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

- **Alt+O**, open a web link in the current message. Land on the message first
  with the number keys or the arrows, then press it. The site's address is read
  out as it opens. If a message holds more than one link, pressing again opens
  the next. Only http and https addresses are ever opened.
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

Alt+M skips all of that. It runs a line editor of its own, with a real caret and
a real selection, and speaks the feedback itself, because nothing else will.
While it is open, Hearthstone Access is told to stand down using its own
`AllowTextInput` mechanism, the same one it uses for deck code entry, so no
keystroke leaks through to the game.

Why the plugin has to do the talking: Unity exposes nothing at all to MSAA or UI
Automation. There is no accessibility layer to plug into, so even a genuine
Unity text field would be as silent to a screen reader as a bare string is.
Hearthstone Access hits the same wall and answers it the same way. The editor
here therefore copies NVDA's behaviour deliberately rather than inventing its
own: arrowing speaks the character you land on, the position past the last
character reads as "blank", word movement speaks the whole word.

Two limits worth knowing:

- You can only reply to people already in the buffer. The plugin learns who
  someone actually is from the whisper itself, so it cannot start a brand new
  conversation with somebody who has not messaged you.
- Typing feedback is spoken by the plugin rather than by your screen reader's
  own edit box handling. It does follow your NVDA settings though, see below.
  Caret movement is always spoken whatever those say, because that is
  navigation rather than echo, and NVDA always speaks it too.

### It follows your NVDA typing echo

You should not have to set the same preference twice, so by default the reply
box reads NVDA's own **Speak typed characters** and **Speak typed words**
settings and matches them. Turn it off with `FollowScreenReaderEcho` if you
would rather set the plugin's own `EchoTypedCharacters` and `EchoTypedWords` by
hand.

NVDA offers three states for each, and all three are honoured:

- **Off**, and the box says nothing as you type.
- **Only in edit controls**, and the box speaks. This is the case worth
  explaining. NVDA looks at what has the focus, sees a Unity window rather than
  an edit field, and stays quiet, so the plugin is the only thing that can
  speak, and it does.
- **Always**, and the box stays quiet, because NVDA speaks typed characters in
  every window in that mode including this one. It is already doing the job,
  and echoing as well would say every character twice.

Two things to know. NVDA only writes these settings to disk when it exits, or
when you press NVDA+Control+C, so toggling echo mid-session with NVDA+2 is not
noticed until it has been saved. And configuration profiles are not read, only
your main settings, so a profile that changes typing echo for Hearthstone
specifically will not be picked up.

None of this applies to the native text box below. That really is an edit
control, so NVDA handles the echo itself, under its own settings, correctly.

### A real text box, if you want to try it

There is one way to get a box your screen reader genuinely owns, and it is in
here behind `UseNativeTextBox` in the config. Turn it on and Alt+M opens an
actual Windows edit control over the game and gives it the keyboard focus. NVDA
then reads it the way it reads any edit field anywhere: its own caret reporting,
its own punctuation and keyboard echo settings, the review cursor, and clipboard
keys that work because Windows implements them rather than because this plugin
reimplemented them.

It is a child window of the Hearthstone window, so it takes the focus without
deactivating the game and a full screen client will not minimise underneath you.

It is still a foreign control inside a game that does not expect one, which is
why it is off by default. If the control cannot be created the plugin falls
back to its own editor without a word, and Escape closes the box either way.
Please say how you get on with it.

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
- `FollowScreenReaderEcho`, default true. Take typing echo from NVDA's own
  settings rather than from the two below. See "It follows your NVDA typing
  echo" above.
- `EchoTypedCharacters`, default false. Speak every character as you type a
  reply. Accurate but chatty. Only used when `FollowScreenReaderEcho` is false,
  or NVDA is not running.
- `EchoTypedWords`, default true. Speak each word as you finish it. Same
  proviso.
- `UseNativeTextBox`, default false. Experimental. Replies open in a real
  Windows edit control that your screen reader reads directly, instead of the
  plugin's own editor. See "A real text box, if you want to try it" above.

## Known limitations

- **Braille is untested.** The braille paths are written against the documented
  Tolk API but have never been exercised against real hardware, because the
  author does not have a display. They are built to fail quietly rather than
  break anything else. Feedback very welcome.
- **The native text box is experimental.** `UseNativeTextBox` is off by default
  because a Win32 control living inside a Unity game is unproven ground. It is
  built to fail safely: if the control will not open you get the plugin's own
  editor instead, and Escape always closes the reply box whichever one is up.
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

## Not affiliated with Hearthstone Access

While HS Message runs on top of Hearthstone Access, it is a separate downstream
project. It contains no Hearthstone Access code and is not endorsed by that
project or by Blizzard. Please do not report problems with it to the Hearthstone
Access developers. Open an issue here instead.

## A word on risk

HS Message reads chat text, keeps it in memory, and optionally writes it to a
local file. It sends nothing anywhere, touches no network, and gives no gameplay
advantage.

It is still a third party modification of the client, installed through a plugin
loader that it brings with it, and that carries some risk to your account.

Hearthstone Access has years of use behind it. This is a new project and has
none of that history, so please do not assume it inherits any of it. Decide
accordingly.

## AI code disclosure

Parts of HS Message were written with AI assistance, alongside code written by
hand. All of it is directed, reviewed and tested by a person, and the parts that
have not been tested say so under Known limitations.

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
