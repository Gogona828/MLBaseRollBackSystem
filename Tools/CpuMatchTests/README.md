# CPU network input regression checks

Run `dotnet run --project Tools/CpuMatchTests/CpuMatchTests.csproj` with .NET 10.
The checks compile the production NetworkInputSender with small Unity/network stubs.
They verify handshake gating, P2 packet ownership, buffered input history, one CPU
step per simulation frame, neutral input during intros, and round reset/resume.

These checks do not run Unity or the actual BattleAI policy. For an integration
check, build with `CombatGame > Build CPU Client`, start the master scene, and
select VS CPU. Confirm both clients advance rounds, only the human responds to
A/D/Space, the delay indicator appears every 8–10 seconds, and Escape/Play stop
closes the spawned client and relay. Also test an absent Python executable and
closing the opponent window; the parent should return to the title with an error.
