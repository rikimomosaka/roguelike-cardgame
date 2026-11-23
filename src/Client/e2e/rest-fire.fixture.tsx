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
      healPreview={{
        currentHpBefore: 30,
        maxHpBefore: 80,
        basePercent: 30,
        baseHealAmount: 24,
        passiveBonusAmount: 5,
        currentHpAfter: 59,
        maxHpAfter: 80,
      }}
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
