# Footsies

## LAN対戦（Mac中継サーバー＋FEP教師あり予測）

Unityの `CombatGame > LAN Match Settings` でMacのLAN IPと接続ポート（P1: 6000 / P2: 6001）を設定します。起動方法・モデル仕様は [Tools/MacLanRelay/README.md](../Tools/MacLanRelay/README.md) を参照してください。

## VS CPU（同じPCで2クライアント対戦）

初回はPlayを停止し、`CombatGame > Build CPU Client` を実行してください。
以後タイトルの **VS CPU** を選ぶと、同じPCに2P用のゲームプロセスと専用リレーを自動起動します。
ゲームのコードやシーンを変更した場合はCPU Clientを再ビルドしてください。
ビルドしたゲームからは自分自身をもう1つ起動します。Mac / Windows / LinuxのStandaloneが対象です。
`Build CPU Client` のMacビルドは **Intel 64-bit（x86_64）専用**です。
ビルド中だけアーキテクチャをx64に指定し、終了後（失敗時も含む）は元の設定へ戻します。

リレー用に **Python 3.10以上** が必要です。既定はMac/Linuxで `python3`、Windowsで `python`。
PATHで見つからない場合はUnity起動前に `COMBATGAME_PYTHON` に実行ファイルの絶対パスを設定してください。
リレーのスクリプトはビルド時に自動同梱します。

人間は1P（A / D / Space）、別ウィンドウの2Pは既存のBattleAIが操作します。
CPUの入力はシミュレーションフレームごとに一度生成してUDP送信し、再シミュレーションには記録した入力を使います。
通常のLAN対戦と同じ3フレーム入力バッファ、FEP推論、入力確定後のロールバック、ラウンド開始同期を使用します。
FEPは毎フレーム評価し、対戦中に対象フレームの確定入力が未到着の場合だけ予測入力を適用します。
専用リレーは通常即時転送し、8〜10秒ごとに100msの間欠遅延を発生させます。
LANの保存設定は変更せず、localhost上の空いている偶数ポートのペアを使います。
タイトルへ戻る・ゲーム終了・EditorのPlay停止で、起動したCPUとリレーを終了します。
起動失敗・45秒以内に接続できない場合はタイトルにエラーを表示します。
CPUのログは `Application.persistentDataPath` の `cpu-client-<port>.log` に出力します。

FOOTSIES is a 2D fighting game where players can control character movement horizontally 
and use one attack button to perform normal and special moves to defeat their opponent.
While the controls (and graphics) are super simple, 
FOOTSIES retains the fundamental feeling of fighting game genre 
where spacing, hit confirm and whiff punish are keys to achieve victory.

<img class="row-picture" src="https://hifight.github.io/static/img/footsies/footsies_00.jpg">

This is a fun little project for the fighting game community and 
for own practice developing a game by myself.
I am objectively bad at art and music but that went kinda well with theme of the game. 
The animation in this game are, obviously, heavily inspired by the most iconic you-know-who fighting game character.

Although I only tested this game mostly with CPU, when I actually tried this game with my friend, 
we actually had a lot of fun! So, I hope that everyone try this game and have some fun as well :D

<img class="row-picture" src="https://hifight.github.io/static/img/footsies/footsies_01.jpg">

<h3>Download</h3> 

<b><u><a href="https://github.com/hifight/Footsies/releases" download>FOOTSIES</a></u></b>

※You can config keys/buttons input when the game is launched, although XInput can't be set on config windows, 
XInput controller should work fine in the game.


<img class="row-picture" src="https://hifight.github.io/static/img/footsies/footsies_03.jpg">


<h3>Mechanics</h3> 
- There is no health bar. Each connected attack removes one Guard, and losing all three Guards loses the round.
- A special move that connects without being blocked causes an immediate K.O. regardless of the remaining Guards.
- There are two type of normal moves, neutral attack and forward/backward attack.
- There are two type of special moves which can be performed by holding and then release attack button.
One can be performed by neutral release, and forward/backward release for the other one.
- If normal moves connect with the opponent, whether on hit or block, it can be canceled into neutral special move by pressing an attack button again.
- Forward and backward dashes can be performed by pressing forward/backward twice.
- Hitbox/hurtbox/frame information can be toggle on and off by pressing F12.
- Press F1 to pause/resume the game. While pausing, pressing F2 will play the game for 1 frame.


<img class="row-picture" src="https://hifight.github.io/static/img/footsies/footsies_04.jpg">


Whether you like the game or not, feel free to leave some comments about your experience on my <b><u><a href="https://twitter.com/">twitter</a></u></b>

If you like the game then invite your friend to play this game too! Seeing some tournament for this game would be a dream come true for me.
