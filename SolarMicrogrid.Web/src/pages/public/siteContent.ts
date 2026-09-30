/** Public contact details shown in the site header, footer and Contact page. */
export const contactDetails = {
  phoneDisplay: '078 293 9038',
  phoneInternational: '+94 78 293 9038',
  phoneHref: 'tel:+94782939038',
  whatsappHref: 'https://wa.me/94782939038',
  email: 'hello@solargridexchange.lk',
  supportEmail: 'support@solargridexchange.lk',
  addressLines: ['No. 42, Galle Road', 'Colombo 03', 'Sri Lanka'],
  hours: [
    { days: 'Monday to Friday', time: '8.30 am to 5.30 pm' },
    { days: 'Saturday', time: '9.00 am to 1.00 pm' },
    { days: 'Sunday and public holidays', time: 'Closed' },
  ],
} as const

export const publicNavigation = [
  { label: 'Home', to: '/' },
  { label: 'Features', to: '/#features' },
  { label: 'How it works', to: '/#how-it-works' },
  { label: 'About', to: '/about' },
  { label: 'Contact', to: '/contact' },
] as const
