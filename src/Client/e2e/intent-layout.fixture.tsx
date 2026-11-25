import { createRoot } from 'react-dom/client'
import { TooltipHost } from '../src/components/Tooltip'
import { IntentChipRow, type IntentDemo } from '../src/screens/BattleScreen'

const intents: IntentDemo[] = [
  {
    kind: 'attack',
    icon: '/assets/icons/intent/attack.png',
    name: '攻撃',
    desc: '複数範囲への攻撃。',
    attack: {
      single: 33,
      singleHits: 2,
      random: 12,
      randomHits: 3,
      all: 7,
      allHits: 1,
    },
  },
  {
    kind: 'defend',
    icon: '◆',
    num: 9,
    name: '防御',
    desc: '9ブロック。',
  },
]

export async function mountIntentLayoutFixture() {
  document.getElementById('intent-layout-fixture')?.remove()

  const host = document.createElement('div')
  host.id = 'intent-layout-fixture'
  Object.assign(host.style, {
    position: 'fixed',
    inset: '0',
    background: '#173226',
  })
  document.body.append(host)

  createRoot(host).render(
    <TooltipHost>
      <div style={{ position: 'relative', width: 100, height: 260, margin: '190px auto 0' }}>
        <IntentChipRow intents={intents} />
      </div>
    </TooltipHost>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  await document.fonts.ready
  document.documentElement.dataset.intentLayoutReady = 'true'
}
