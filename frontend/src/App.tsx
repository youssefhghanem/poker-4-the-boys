import { BrowserRouter, Routes, Route } from 'react-router-dom'
import { HomeScreen } from './screens/Home/Home'
import { CreateGameScreen } from './screens/CreateGame/CreateGame'
import { JoinGameScreen } from './screens/JoinGame/JoinGame'
import { LobbyScreen } from './screens/Lobby/Lobby'
import { GameTableScreen } from './screens/GameTable/GameTable'
import { GameEndScreen } from './screens/GameEnd/GameEnd'
import { SignalRProvider } from './SignalRProvider'

function App() {
  return (
    <BrowserRouter>
      <SignalRProvider>
        <Routes>
          <Route path="/" element={<HomeScreen />} />
          <Route path="/create" element={<CreateGameScreen />} />
          <Route path="/join" element={<JoinGameScreen />} />
          <Route path="/lobby" element={<LobbyScreen />} />
          <Route path="/game" element={<GameTableScreen />} />
          <Route path="/game-end" element={<GameEndScreen />} />
        </Routes>
      </SignalRProvider>
    </BrowserRouter>
  )
}

export default App
