# Product

<!-- impeccable:product-schema 1 -->

## Platform

web

## Users

- Conference organizers create conferences, configure calls for proposals, review submissions, coordinate email communications, and publish schedules.
- Speakers create profiles, submit and manage proposals, and check proposal status.
- Reviewers evaluate assigned proposals and record scores and comments.
- Public visitors discover conferences, read proposal and schedule information, and share public links.

## Product Purpose

Provide a managed, low-cost service for conference organizers to run the proposal workflow from CFP setup and submission through review, notifications, and timetable publication.

## Positioning

The product is intended to provide CFP operations comparable in function to platforms such as Fortee. No unique market differentiation or comparative claim has been established; do not invent one.

## Operating Context

Organizers manage separate conferences and assign conference-scoped roles. Speakers and reviewers use authenticated workflows, while published conference information and timetables remain publicly readable. Proposal content and review records can contain personal or confidential information and must remain access-controlled.

## Capabilities and Constraints

- Confirmed scope includes conference creation, configurable proposal types and forms, proposal submission and management, review and decisions, email notifications, timetable planning/publication, and social-profile links or share links.
- Initial sign-in providers are Google and Microsoft accounts through Microsoft Entra External ID. Azure Functions Easy Auth validates API tokens; application code enforces conference-specific authorization.
- The intended stack is Blazor WebAssembly targeting .NET 10 (`net10.0`), .NET 10 isolated Azure Functions, and Azure Cosmos DB for NoSQL. Technical verification favors managed Azure services and low operating cost.
- The technical-verification environment uses Cosmos DB Free Tier in Japan East; Static Web Apps is placed in East Asia, and Azure Communication Services Email uses global resources with Japan data location.
- Social API posting, ticket sales, payments, sponsorship management, and venue check-in are outside the initial scope.
- Product language beyond the current Japanese design documentation, traffic and data-volume targets, retention policy, and service-level objectives remain undecided.

## Brand Commitments

No product name, logo, visual identity, or visual direction has been established. Fortee is a functional reference, not a requirement to copy its branding or interface.

## Evidence on Hand

- [Architecture and product requirements](doc/architecture.md)
- [Cosmos DB data model](doc/database-design.md)
- No running application, approved product copy, logo, or production content is present yet. Future examples must be clearly synthetic and must not imply real events, customers, or performance.

## Product Principles

- Keep operational overhead and baseline Azure cost low while using managed services.
- Enforce permissions at the API boundary and scope organizer and reviewer access to a conference.
- Preserve proposal and review confidentiality while making explicitly published information easy to discover.
- Make important workflow states, errors, and communication outcomes clear to users.

## Accessibility & Inclusion

Design and implementation target WCAG 2.2 AA as a quality goal, including keyboard operation, visible focus, clear form labels and errors, accessible status announcements, responsive Japanese content, and avoiding color-only communication. Conformance requires testing and is not implied by the target.
