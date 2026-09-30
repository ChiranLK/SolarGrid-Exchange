import { useEffect, useRef, useState, type FormEvent } from 'react'
import {
  ArrowRightIcon,
  ChevronDownIcon,
  ClockIcon,
  MailIcon,
  MapPinIcon,
  MessageIcon,
  PhoneIcon,
} from '../../components/icons'
import { contactDetails } from './siteContent'
import { useReveal } from './useReveal'

const topics = ['Getting the app', 'My account', 'A booking', 'Bringing a station to my area', 'Something else']

const faqs = [
  {
    question: 'Who can join SolarGrid Exchange?',
    answer: 'Any household or small business near one of our stations. Register in the Android app with your NIC, phone number, email and address, and our team will review your account.',
  },
  {
    question: 'How long does account approval take?',
    answer: 'We review new accounts during office hours. You can sign in to the app as soon as your account is activated. If it is taking longer than expected, give us a call.',
  },
  {
    question: 'How far ahead can I book energy?',
    answer: 'You can book a slot up to 7 days ahead. Each station shows how much capacity is left in every slot before you book.',
  },
  {
    question: 'Can I change or cancel a booking?',
    answer: 'Yes. You can move or cancel a booking up to 12 hours before it starts. After that it stays as it is, so the slot is not lost to other members.',
  },
  {
    question: 'What do I need to bring to the station?',
    answer: 'Just your phone. Open your approved booking in the app and show the QR pass to the Grid Operator. The code refreshes after a few minutes, so open it when you arrive.',
  },
]

export function ContactPage() {
  const pageRef = useRef<HTMLDivElement>(null)
  useReveal(pageRef)
  const [name, setName] = useState('')
  const [email, setEmail] = useState('')
  const [topic, setTopic] = useState(topics[0])
  const [message, setMessage] = useState('')
  const [submitted, setSubmitted] = useState(false)

  useEffect(() => {
    document.title = 'Contact us | SolarGrid Exchange'
  }, [])

  const isValid = name.trim() !== '' && /\S+@\S+\.\S+/.test(email) && message.trim() !== ''

  function handleSubmit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault()
    if (!isValid) return
    const subject = `${topic} - ${name.trim()}`
    const body = `${message.trim()}\n\nName: ${name.trim()}\nEmail: ${email.trim()}`
    window.location.href = `mailto:${contactDetails.email}?subject=${encodeURIComponent(subject)}&body=${encodeURIComponent(body)}`
    setSubmitted(true)
  }

  const channels = [
    {
      icon: PhoneIcon,
      label: 'Call us',
      value: contactDetails.phoneDisplay,
      note: contactDetails.phoneInternational,
      href: contactDetails.phoneHref,
    },
    {
      icon: MessageIcon,
      label: 'WhatsApp',
      value: contactDetails.phoneDisplay,
      note: 'Messages answered in office hours',
      href: contactDetails.whatsappHref,
      external: true,
    },
    {
      icon: MailIcon,
      label: 'Email',
      value: contactDetails.email,
      note: `Account help: ${contactDetails.supportEmail}`,
      href: `mailto:${contactDetails.email}`,
    },
  ]

  return (
    <div ref={pageRef}>
      <section className="page-intro">
        <div className="site-container">
          <p className="site-kicker"><span className="site-kicker-dot" /> Contact us</p>
          <h1 className="page-intro-title">We are here to help.</h1>
          <p className="page-intro-lead">
            Questions about the app, your account or a booking? Call, send a WhatsApp message or
            write to us. A real person from our Colombo office will get back to you.
          </p>
        </div>
      </section>

      <section className="site-section pt-0">
        <div className="site-container">
          <div className="channel-grid">
            {channels.map((channel, index) => (
              <a
                key={channel.label}
                href={channel.href}
                className="glass-card channel-card"
                data-reveal
                style={{ transitionDelay: `${index * 70}ms` }}
                {...(channel.external ? { target: '_blank', rel: 'noreferrer' } : {})}
              >
                <span className="feature-icon"><channel.icon width={22} height={22} /></span>
                <span className="channel-label">{channel.label}</span>
                <strong>{channel.value}</strong>
                <small>{channel.note}</small>
                <span className="channel-arrow"><ArrowRightIcon width={18} height={18} /></span>
              </a>
            ))}
          </div>

          <div className="contact-grid">
            <div className="glass-card contact-form-card" data-reveal>
              <h2 className="h4 mb-1">Send us a message</h2>
              <p className="text-body-secondary mb-4">This opens your email app with your message ready to send.</p>

              {submitted && (
                <div className="alert alert-success" role="status">
                  Your email app should now be open. If nothing happened, write to us at{' '}
                  <a href={`mailto:${contactDetails.email}`}>{contactDetails.email}</a>.
                </div>
              )}

              <form onSubmit={handleSubmit} noValidate>
                <div className="row g-3">
                  <div className="col-12 col-md-6">
                    <label htmlFor="contact-name" className="form-label">Your name</label>
                    <input
                      id="contact-name"
                      className="form-control"
                      autoComplete="name"
                      value={name}
                      onChange={(event) => setName(event.target.value)}
                      required
                    />
                  </div>
                  <div className="col-12 col-md-6">
                    <label htmlFor="contact-email" className="form-label">Email address</label>
                    <input
                      id="contact-email"
                      type="email"
                      className="form-control"
                      autoComplete="email"
                      value={email}
                      onChange={(event) => setEmail(event.target.value)}
                      required
                    />
                  </div>
                  <div className="col-12">
                    <label htmlFor="contact-topic" className="form-label">What is it about?</label>
                    <select
                      id="contact-topic"
                      className="form-select"
                      value={topic}
                      onChange={(event) => setTopic(event.target.value)}
                    >
                      {topics.map((option) => <option key={option}>{option}</option>)}
                    </select>
                  </div>
                  <div className="col-12">
                    <label htmlFor="contact-message" className="form-label">Message</label>
                    <textarea
                      id="contact-message"
                      className="form-control"
                      rows={5}
                      value={message}
                      onChange={(event) => setMessage(event.target.value)}
                      required
                    />
                  </div>
                  <div className="col-12">
                    <button type="submit" className="btn site-btn site-btn-primary site-btn-lg" disabled={!isValid}>
                      Write the email <ArrowRightIcon width={18} height={18} />
                    </button>
                  </div>
                </div>
              </form>
            </div>

            <aside className="contact-side" aria-label="Office details">
              <div className="glass-card contact-info-card" data-reveal>
                <span className="feature-icon"><MapPinIcon width={22} height={22} /></span>
                <h2 className="h5">Visit our office</h2>
                <address className="mb-0">
                  {contactDetails.addressLines.map((line) => <span key={line}>{line}</span>)}
                </address>
              </div>
              <div className="glass-card contact-info-card" data-reveal>
                <span className="feature-icon"><ClockIcon width={22} height={22} /></span>
                <h2 className="h5">Opening hours</h2>
                <dl className="hours-list mb-0">
                  {contactDetails.hours.map((row) => (
                    <div key={row.days}>
                      <dt>{row.days}</dt>
                      <dd>{row.time}</dd>
                    </div>
                  ))}
                </dl>
              </div>
            </aside>
          </div>
        </div>
      </section>

      <section id="faq" className="site-section site-section-tint site-section-last" aria-labelledby="faq-title">
        <div className="site-container site-container-narrow">
          <div className="site-section-head text-center mx-auto" data-reveal>
            <p className="site-eyebrow">Common questions</p>
            <h2 id="faq-title" className="site-section-title">Before you get in touch</h2>
          </div>
          <div className="faq-list">
            {faqs.map((faq) => (
              <details key={faq.question} className="glass-card faq-item" data-reveal>
                <summary>
                  {faq.question}
                  <ChevronDownIcon width={20} height={20} />
                </summary>
                <p>{faq.answer}</p>
              </details>
            ))}
          </div>
        </div>
      </section>
    </div>
  )
}
