import { createRoot } from 'react-dom/client'
import type { MerchantInventoryDto } from '../src/api/types'
import { TooltipHost } from '../src/components/Tooltip'
import { MerchantScreen } from '../src/screens/MerchantScreen'

declare global {
  interface Window {
    __merchantBuyCalls?: string[]
  }
}

export async function mountMerchantFixture() {
  document.getElementById('merchant-fixture')?.remove()
  const host = document.createElement('div')
  host.id = 'merchant-fixture'
  document.body.append(host)

  const inventory: MerchantInventoryDto = {
    cards: Array.from({ length: 30 }, (_, index) => ({
      kind: 'card' as const,
      id: `offer_card_${String(index + 1).padStart(2, '0')}`,
      price: 40 + index,
      sold: false,
    })),
    relics: [],
    potions: [],
    discardPrice: 75,
    discardSlotUsed: false,
  }

  window.__merchantBuyCalls = []
  createRoot(host).render(
    <TooltipHost>
      <MerchantScreen
        gold={999}
        deck={[]}
        inventory={inventory}
        onBuy={async (kind, id) => {
          window.__merchantBuyCalls?.push(`${kind}:${id}`)
        }}
        onDiscard={async () => {}}
        onLeave={async () => {}}
      />
    </TooltipHost>,
  )

  await new Promise<void>(resolve => {
    requestAnimationFrame(() => requestAnimationFrame(() => resolve()))
  })
  await document.fonts.ready
  document.documentElement.dataset.merchantFixtureReady = 'true'
}
