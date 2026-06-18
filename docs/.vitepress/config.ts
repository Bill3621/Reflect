import { defineConfig } from 'vitepress'

export default defineConfig({
  title: 'Reflect',
  description: 'Client-server networking for Flax Engine',
  lastUpdated: true,
  cleanUrls: true,

  themeConfig: {
    socialLinks: [
      { icon: 'github', link: 'https://github.com/Bill3621/Reflect' }
    ],

    search: {
      provider: 'local'
    },

    nav: [
      { text: 'Guide', link: '/getting-started' },
      { text: 'Reference', link: '/api-reference' },
      { text: 'GitHub', link: 'https://github.com/Bill3621/Reflect' }
    ],

    sidebar: [
      {
        text: 'Introduction',
        items: [
          { text: 'Overview', link: '/' }
        ]
      },
      {
        text: 'Guide',
        items: [
          { text: 'Getting Started', link: '/getting-started' },
          { text: 'Architecture', link: '/architecture' }
        ]
      },
      {
        text: 'Reference',
        items: [
          { text: 'RPC System', link: '/rpc-system' },
          { text: 'SyncVars', link: '/syncvars' },
          { text: 'Transport Layer', link: '/transport' },
          { text: 'API Reference', link: '/api-reference' }
        ]
      }
    ],

    footer: {
      message: 'Released under the MIT License.',
      copyright: 'Copyright © 2026 Bill'
    }
  }
})
