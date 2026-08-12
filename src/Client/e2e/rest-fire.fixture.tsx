import { createRoot } from 'react-dom/client'
import { RestScreen } from '../src/screens/RestScreen'

export async function mountRestFireFixture() {
  document.getElementById('rest-fire-fixture')?.remove()
  const host = document.createElement('div')
  host.id = 'rest-fire-fixture'
  document.body.append(host)

  createRoot(host).render(
    <RestScreen
      deck={[]}
      completed={false}
      onHeal={() => undefined}
      onUpgrade={() => undefined}
      onClose={() => undefined}
    />,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  document.documentElement.dataset.restFireReady = 'true'
}
