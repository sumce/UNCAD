# UNCAD Pro 2.5.1

Release date: 2026-10-11

This release fixes a serious usability regression introduced in 2.5.0: when the
account was missing or invalid, the plugin could **prevent the user from working in
AutoCAD at all**, and it gave no way to sign in again.

## Authorization problems no longer block AutoCAD

- **The periodic license check no longer opens a modal window.** In 2.5.0 an
  invalid/missing authorization queued a login request that the Idle handler
  displayed with an application-modal dialog — while that window was open the user
  could not use AutoCAD at all. Sign-in is now only offered when *the user* acts.
- **License messages are written to the command line instead of a modal message box.**
  The message box was also application-modal, so it blocked AutoCAD, and its dedup
  key changed whenever the underlying error text differed (for example flaky network
  error strings), which could re-open it as often as every poll.
- **Authorization problems only disable UNCAD commands.** AutoCAD's own drawing,
  editing and saving are unaffected, and the blocked-command message now says so
  explicitly.

## You can always get back to a working state

- Every blocked UNCAD command now offers the way out: it names the exact command to
  run (`UNC_LOGIN`) and opens the sign-in window, where you can either sign in **or
  register a new account**.
- **The sign-in prompt is no longer permanently suppressed.** In 2.5.0 it was
  deduplicated by (account, reason), so once it had been shown and dismissed the
  window never appeared again — the user was left with only an error message and no
  way to sign in. A user-initiated action now always re-offers sign-in.
- `UNC_LOGIN` continues to work even when the license is invalid (it is deliberately
  not gated by the license check).

## Clearer message when the account is gone

The blocked-command text now tells the user what to do instead of only reporting the
server's reason, for example:

```
账号不存在或已被删除，请联系UNSIAO.Ltd处理。
请在 AutoCAD 中运行 UNC_LOGIN 登录或注册账号，然后重新执行命令“U1F”。
（授权问题只停用 UNCAD 命令，不影响 AutoCAD 本身绘图与保存。）
```

## Regression coverage

`tests/UNCAD.Tests/LicenseUxContractTests.cs` locks the behaviour so it cannot be
reverted:

- a periodic heartbeat must not queue a modal sign-in request;
- a user-initiated sign-in request must never be permanently deduplicated
  (repeated attempts all succeed);
- the blocked-command message must name `UNC_LOGIN` and state that AutoCAD itself is
  unaffected;
- the `UNC_LOGIN` command must not be gated by the license check.

## Upgrade note

Nothing to do. Install 2.5.1 over 2.5.0; an existing device binding keeps working.
If your account was removed on the server, sign in with `UNC_LOGIN` and either use an
account provided by UNSIAO.Ltd or register a new one — a new account needs an
authorization granted by an administrator before UNCAD commands become available.
