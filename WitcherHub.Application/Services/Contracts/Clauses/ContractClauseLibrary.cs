namespace WitcherHub.Application.Services.Contracts.Clauses
{
    /// <summary>
    /// The released legal wording, as modules.
    ///
    /// Every general clause in a WitcherHub contract comes from here. The model
    /// does not write them and cannot change what they mean; it may only say
    /// which of them fit the work being contracted, and even that choice is
    /// checked against each module's own applicability rules before anything is
    /// assembled.
    ///
    /// The wording is drafted for German B2B agency and service contracts and is
    /// reformulated rather than copied from any reference document. It respects
    /// three limits throughout, because breaking any of them makes a clause
    /// worthless in exactly the situation it was written for:
    ///
    ///   * liability for intent, gross negligence, and injury to life, body or
    ///     health is never excluded or capped;
    ///   * the customer's indemnity is tied to the customer's own breach, never
    ///     open-ended;
    ///   * nothing invents a contractual penalty, a limitation period or a
    ///     jurisdiction that the structured data does not support.
    ///
    /// Every module here is <see cref="ClauseReviewStatus.PendingLegalReview"/>.
    /// That is deliberate and it has teeth: a contract carrying an unreviewed
    /// module can be drafted and read, but cannot be approved. A German lawyer
    /// releases them by changing the status and bumping the version.
    /// </summary>
    public static class ContractClauseLibrary
    {
        /// <summary>
        /// Recorded on every generated version beside the prompt version, so it
        /// stays answerable which wording a given contract was built from.
        /// Bumped whenever any module's text or applicability changes.
        /// </summary>
        public const string LibraryVersion = "1.0.0";

        private static readonly ClauseModule[] Modules =
        [
            // ────────────────────────────────── Gegenstand und Zustandekommen

            new()
            {
                Id = "GENERAL_SCOPE",
                Version = 1,
                SectionOrder = 100,
                Title = "Geltungsbereich und Vertragsgegenstand",
                DefaultSelected = true,
                Text =
                    "(1) Dieser Vertrag regelt die Erbringung der in der Leistungsbeschreibung " +
                    "(Anlage A) im Einzelnen bezeichneten Leistungen des Anbieters für den Kunden.\n\n" +
                    "(2) Geschuldet ist ausschließlich der dort ausdrücklich vereinbarte Leistungsumfang. " +
                    "Leistungen, die dort nicht aufgeführt sind, sind nicht Vertragsgegenstand.\n\n" +
                    "(3) Ergänzungen und Abweichungen von diesem Vertrag werden nur wirksam, wenn die " +
                    "Parteien sie ausdrücklich vereinbaren."
            },

            new()
            {
                Id = "CONTRACT_FORMATION",
                Version = 1,
                SectionOrder = 110,
                Title = "Vertragsschluss",
                DefaultSelected = true,
                Text =
                    "(1) Angebote des Anbieters sind freibleibend, sofern sie nicht ausdrücklich als " +
                    "verbindlich bezeichnet sind.\n\n" +
                    "(2) Der Vertrag kommt zustande, sobald beide Parteien dieses Dokument unterzeichnet " +
                    "haben.\n\n" +
                    "(3) Kommt kein Vertrag zustande, verbleiben Entwürfe, Muster und gestalterische " +
                    "Vorschläge des Anbieters einschließlich zugehöriger Dateien bei diesem. Der Kunde " +
                    "erwirbt hieran keine Rechte und gibt erhaltene Exemplare auf Verlangen zurück oder " +
                    "löscht sie."
            },

            // ────────────────────────────────── Mitwirkung, Änderungen, Abnahme

            new()
            {
                Id = "CUSTOMER_COOPERATION",
                Version = 1,
                SectionOrder = 200,
                Title = "Mitwirkungs- und Beistellungspflichten des Kunden",
                DefaultSelected = true,
                Text =
                    "(1) Der Kunde stellt dem Anbieter die zur Leistungserbringung erforderlichen " +
                    "Informationen, Unterlagen, Inhalte, Daten und Zugänge rechtzeitig, vollständig und " +
                    "in geeigneter Form zur Verfügung und benennt eine entscheidungsbefugte " +
                    "Ansprechperson.\n\n" +
                    "(2) Der Kunde ist dafür verantwortlich, dass die von ihm bereitgestellten Inhalte " +
                    "richtig und vollständig sind und dass ihm die für deren Nutzung erforderlichen " +
                    "Rechte zustehen. Eine Prüfung dieser Inhalte auf rechtliche Zulässigkeit schuldet " +
                    "der Anbieter nicht.\n\n" +
                    "(3) Kommt der Kunde seinen Mitwirkungspflichten nicht oder nicht rechtzeitig nach, " +
                    "verschieben sich davon abhängige Termine um den entsprechenden Zeitraum zuzüglich " +
                    "einer angemessenen Wiederanlauffrist. Mehraufwand, der dem Anbieter hierdurch " +
                    "entsteht, ist nach Aufwand zu vergüten.",
                LegalNote =
                    "Absatz 3 verschiebt Termine und begründet einen Vergütungsanspruch. Bitte prüfen, " +
                    "ob die Formulierung als AGB der Inhaltskontrolle standhält."
            },

            new()
            {
                Id = "CHANGE_REQUESTS",
                Version = 1,
                SectionOrder = 210,
                Title = "Änderungswünsche und Zusatzleistungen",
                DefaultSelected = true,
                Text =
                    "(1) Wünscht der Kunde Leistungen, die über den vereinbarten Leistungsumfang " +
                    "hinausgehen, teilt er dies dem Anbieter mit. Der Anbieter unterbreitet hierzu ein " +
                    "Angebot mit Aufwand, Vergütung und Auswirkungen auf vereinbarte Termine.\n\n" +
                    "(2) Zusatzleistungen werden erst geschuldet, wenn die Parteien sich über Umfang und " +
                    "Vergütung geeinigt haben. Bis dahin bleibt der ursprüngliche Leistungsumfang " +
                    "maßgeblich."
            },

            new()
            {
                Id = "REVISION_ROUNDS",
                Version = 1,
                SectionOrder = 220,
                Title = "Korrekturschleifen",
                RequiredFields = ["RevisionRounds"],
                Text =
                    "(1) Dem Kunden stehen {{RevisionRounds}} Korrekturschleifen je Liefergegenstand zu. " +
                    "Korrekturen betreffen die Überarbeitung des vorgelegten Ergebnisses, nicht dessen " +
                    "Neuerstellung.\n\n" +
                    "(2) Weitergehende Änderungswünsche werden nach Aufwand vergütet.",
                LegalNote =
                    "Die Zahl der Korrekturschleifen muss aus den Vertragsdaten stammen. Ohne diesen " +
                    "Wert wird die Klausel nicht ausgegeben."
            },

            new()
            {
                Id = "ACCEPTANCE_WORK",
                Version = 1,
                SectionOrder = 230,
                Title = "Abnahme",
                AppliesToNatures = [ServiceNature.Work, ServiceNature.Mixed],
                RequiredFields = ["AcceptancePeriodDays"],
                Text =
                    "(1) Soweit der Anbieter abnahmefähige Werkleistungen schuldet, legt er das " +
                    "fertiggestellte Ergebnis dem Kunden zur Abnahme vor.\n\n" +
                    "(2) Der Kunde prüft das Ergebnis innerhalb von {{AcceptancePeriodDays}} Tagen und " +
                    "erklärt die Abnahme oder verweigert sie unter Angabe der Gründe in Textform.\n\n" +
                    "(3) Unwesentliche Abweichungen berechtigen nicht zur Verweigerung der Abnahme.",
                LegalNote =
                    "Eine Abnahmefiktion bei Schweigen ist hier bewusst nicht vorgesehen. § 640 Abs. 2 " +
                    "BGB verlangt dafür eine Aufforderung in Textform mit Hinweis auf die Folgen; ob " +
                    "und wie das abgebildet wird, ist eine anwaltliche Entscheidung."
            },

            // ────────────────────────────────── Vergütung und Zahlung

            new()
            {
                Id = "PAYMENT_TERMS",
                Version = 1,
                SectionOrder = 300,
                Title = "Vergütung, Umsatzsteuer und Nebenkosten",
                DefaultSelected = true,
                RequiredFields = ["PaymentDueDays"],
                Text =
                    "(1) Die Vergütung ergibt sich aus der Preisübersicht dieses Vertrages. Alle Beträge " +
                    "verstehen sich netto zuzüglich der jeweils geltenden gesetzlichen Umsatzsteuer.\n\n" +
                    "(2) Rechnungen sind ohne Abzug innerhalb von {{PaymentDueDays}} Tagen ab Zugang zur " +
                    "Zahlung fällig.\n\n" +
                    "(3) Kosten Dritter — insbesondere Werbebudgets, Lizenzen, Stockmaterial, Hosting, " +
                    "Domains, Werkzeuge und Reisekosten — sind in der Vergütung nur enthalten, soweit " +
                    "dies ausdrücklich vereinbart ist. Im Übrigen werden sie gesondert berechnet."
            },

            new()
            {
                Id = "PAYMENT_DEFAULT",
                Version = 1,
                SectionOrder = 310,
                Title = "Zahlungsverzug und Leistungsaussetzung",
                DefaultSelected = true,
                Text =
                    "(1) Gerät der Kunde mit einer fälligen Zahlung in Verzug, gelten die gesetzlichen " +
                    "Verzugsregelungen.\n\n" +
                    "(2) Der Anbieter kann seine Leistungen nach vorheriger Ankündigung in Textform und " +
                    "nach fruchtlosem Ablauf einer angemessenen Frist aussetzen, solange eine fällige " +
                    "Zahlung offen ist oder erforderliche Informationen, Zugänge oder Freigaben des " +
                    "Kunden fehlen. Vereinbarte Termine verschieben sich entsprechend.",
                LegalNote =
                    "Das Leistungsverweigerungsrecht ist an Ankündigung und Fristsetzung gebunden. " +
                    "Bitte anwaltlich prüfen, ob dies für die konkrete Vertragsart angemessen ist."
            },

            // ────────────────────────────────── Laufzeit und Beendigung

            new()
            {
                Id = "TERM_FIXED",
                Version = 1,
                SectionOrder = 400,
                Title = "Vertragsbeginn und Laufzeit",
                AppliesToRecurrences = [ServiceRecurrence.OneTime],
                RequiredFields = ["ServiceStartDate"],
                IncompatibleWith = ["TERM_RECURRING"],
                Text =
                    "(1) Der Vertrag beginnt am {{ServiceStartDate}}.\n\n" +
                    "(2) Er endet, ohne dass es einer Kündigung bedarf, mit der vollständigen Erbringung " +
                    "der vereinbarten Leistungen."
            },

            new()
            {
                Id = "TERM_RECURRING",
                Version = 1,
                SectionOrder = 400,
                Title = "Vertragsbeginn, Laufzeit und Verlängerung",
                AppliesToRecurrences = [ServiceRecurrence.Recurring, ServiceRecurrence.Mixed],
                RequiredFields = ["ServiceStartDate", "MinimumTermMonths", "NoticePeriodDays"],
                IncompatibleWith = ["TERM_FIXED"],
                Text =
                    "(1) Der Vertrag beginnt am {{ServiceStartDate}} und hat eine Mindestlaufzeit von " +
                    "{{MinimumTermMonths}} Monaten.\n\n" +
                    "(2) Er kann von beiden Parteien mit einer Frist von {{NoticePeriodDays}} Tagen zum " +
                    "Ende der Mindestlaufzeit gekündigt werden.",
                LegalNote =
                    "Eine automatische Verlängerung ist hier bewusst nicht enthalten. Sie wird nur " +
                    "ausgegeben, wenn AutoRenewalEnabled gesetzt ist und das Modul " +
                    "TERM_AUTO_RENEWAL ausgewählt wurde."
            },

            new()
            {
                Id = "TERM_AUTO_RENEWAL",
                Version = 1,
                SectionOrder = 410,
                Title = "Automatische Verlängerung",
                AppliesToRecurrences = [ServiceRecurrence.Recurring, ServiceRecurrence.Mixed],
                RequiredFields = ["RenewalTermMonths", "NoticePeriodDays"],
                Text =
                    "(1) Der Vertrag verlängert sich nach Ablauf der Mindestlaufzeit jeweils um " +
                    "{{RenewalTermMonths}} Monate, wenn er nicht mit einer Frist von " +
                    "{{NoticePeriodDays}} Tagen zum jeweiligen Laufzeitende gekündigt wird.\n\n" +
                    "(2) Die Kündigung bedarf der Textform.",
                LegalNote =
                    "Automatische Verlängerungen unterliegen auch im B2B-Verkehr der AGB-Kontrolle. " +
                    "Laufzeit und Kündigungsfrist müssen angemessen sein — anwaltlich zu prüfen."
            },

            new()
            {
                Id = "ORDINARY_TERMINATION",
                Version = 1,
                SectionOrder = 420,
                Title = "Ordentliche Kündigung",
                AppliesToRecurrences = [ServiceRecurrence.Recurring, ServiceRecurrence.Mixed],
                RequiredFields = ["NoticePeriodDays"],
                Text =
                    "Nach Ablauf einer etwaigen Mindestlaufzeit kann der Vertrag von beiden Parteien " +
                    "mit einer Frist von {{NoticePeriodDays}} Tagen in Textform gekündigt werden."
            },

            new()
            {
                Id = "TERMINATION_FOR_CAUSE",
                Version = 1,
                SectionOrder = 430,
                Title = "Außerordentliche Kündigung",
                DefaultSelected = true,
                Text =
                    "(1) Das Recht beider Parteien zur Kündigung aus wichtigem Grund bleibt unberührt.\n\n" +
                    "(2) Ein wichtiger Grund liegt für den Anbieter insbesondere vor, wenn der Kunde mit " +
                    "einer wesentlichen Zahlung trotz Fristsetzung erheblich in Verzug bleibt oder " +
                    "erforderliche Mitwirkungshandlungen trotz angemessener Fristsetzung dauerhaft " +
                    "unterlässt.\n\n" +
                    "(3) Die Kündigung bedarf der Textform."
            },

            new()
            {
                Id = "TERMINATION_CONSEQUENCES",
                Version = 1,
                SectionOrder = 440,
                Title = "Folgen der Vertragsbeendigung",
                DefaultSelected = true,
                Text =
                    "(1) Mit Beendigung des Vertrages rechnet der Anbieter die bis dahin vertragsgemäß " +
                    "erbrachten Leistungen ab.\n\n" +
                    "(2) Zugänge und überlassene Unterlagen werden auf Verlangen zurückgegeben oder " +
                    "gelöscht, soweit keine gesetzlichen Aufbewahrungspflichten entgegenstehen.\n\n" +
                    "(3) Bereits eingeräumte Nutzungsrechte an vollständig vergüteten Arbeitsergebnissen " +
                    "bleiben bestehen."
            },

            // ────────────────────────────────── Mängel, Haftung, Freistellung

            new()
            {
                Id = "WARRANTY_WORK",
                Version = 1,
                SectionOrder = 500,
                Title = "Mängelanzeige und Nacherfüllung",
                AppliesToNatures = [ServiceNature.Work, ServiceNature.Mixed],
                Text =
                    "(1) Der Kunde zeigt Mängel nach Feststellung unverzüglich in Textform an und " +
                    "beschreibt sie so, dass sie nachvollzogen werden können.\n\n" +
                    "(2) Der Anbieter leistet Nacherfüllung. Schlägt die Nacherfüllung fehl, stehen dem " +
                    "Kunden die gesetzlichen Rechte zu.\n\n" +
                    "(3) Unwesentliche Abweichungen begründen keine Mängelansprüche.",
                LegalNote =
                    "Eine Verkürzung der Verjährungsfrist ist hier bewusst nicht enthalten. Der " +
                    "Referenzvertrag verkürzt auf ein Jahr; ob das im konkreten Fall wirksam ist, ist " +
                    "eine anwaltliche Entscheidung und wäre als eigenes Modul aufzunehmen."
            },

            new()
            {
                Id = "SERVICE_NO_SUCCESS_GUARANTEE",
                Version = 1,
                SectionOrder = 510,
                Title = "Keine Erfolgsgarantie",
                AppliesToNatures = [ServiceNature.Service, ServiceNature.Mixed],
                Text =
                    "(1) Die vom Anbieter erbrachten Beratungs-, Analyse-, Monitoring- und " +
                    "Marketingleistungen sind Dienstleistungen im Sinne der §§ 611 ff. BGB. Geschuldet " +
                    "ist die fachgerechte Erbringung der vereinbarten Tätigkeit, nicht ein bestimmter " +
                    "wirtschaftlicher Erfolg.\n\n" +
                    "(2) Ein bestimmtes Ranking, eine bestimmte Sichtbarkeit, ein bestimmter " +
                    "Traffic-, Umsatz-, Reichweiten- oder Lead-Wert sowie die Freigabe von Inhalten " +
                    "durch Dritte werden nicht geschuldet und nicht zugesichert."
            },

            new()
            {
                Id = "THIRD_PARTY_PLATFORM_DEPENDENCY",
                Version = 1,
                SectionOrder = 520,
                Title = "Abhängigkeit von Drittplattformen",
                AppliesToNatures = [ServiceNature.Service, ServiceNature.Mixed],
                Text =
                    "(1) Die Leistungen des Anbieters hängen teilweise von Systemen, Schnittstellen, " +
                    "Algorithmen und Richtlinien Dritter ab, insbesondere von Suchmaschinen, sozialen " +
                    "Netzwerken, Analyse- und Werbeplattformen.\n\n" +
                    "(2) Änderungen, Einschränkungen oder Ausfälle auf Seiten dieser Dritten liegen " +
                    "außerhalb des Einflussbereichs des Anbieters. Daraus folgende Abweichungen in " +
                    "Daten, Verfügbarkeit oder Ergebnissen stellen keinen Mangel dar."
            },

            new()
            {
                Id = "LIABILITY_B2B",
                Version = 1,
                SectionOrder = 530,
                Title = "Haftung",
                DefaultSelected = true,
                Text =
                    "(1) Der Anbieter haftet unbeschränkt für Vorsatz und grobe Fahrlässigkeit, für " +
                    "Schäden aus der Verletzung des Lebens, des Körpers oder der Gesundheit, im Rahmen " +
                    "einer übernommenen Garantie sowie nach dem Produkthaftungsgesetz.\n\n" +
                    "(2) Bei leicht fahrlässiger Verletzung einer wesentlichen Vertragspflicht — also " +
                    "einer Pflicht, deren Erfüllung die ordnungsgemäße Durchführung des Vertrages " +
                    "überhaupt erst ermöglicht und auf deren Einhaltung der Kunde regelmäßig vertrauen " +
                    "darf — haftet der Anbieter der Höhe nach begrenzt auf den bei Vertragsschluss " +
                    "vorhersehbaren, vertragstypischen Schaden.\n\n" +
                    "(3) Im Übrigen ist die Haftung für leichte Fahrlässigkeit ausgeschlossen.\n\n" +
                    "(4) Die vorstehenden Regelungen gelten auch für die Haftung für gesetzliche " +
                    "Vertreter und Erfüllungsgehilfen des Anbieters.",
                LegalNote =
                    "Zwingende Haftungstatbestände sind ausgenommen. Eine betragsmäßige Obergrenze ist " +
                    "bewusst nicht enthalten — falls gewünscht, ist sie als eigenes Modul mit " +
                    "anwaltlicher Freigabe aufzunehmen."
            },

            new()
            {
                Id = "THIRD_PARTY_INDEMNITY",
                Version = 1,
                SectionOrder = 540,
                Title = "Freistellung bei Ansprüchen Dritter",
                DefaultSelected = true,
                Text =
                    "(1) Macht ein Dritter gegenüber dem Anbieter Ansprüche geltend, die darauf beruhen, " +
                    "dass vom Kunden bereitgestellte Inhalte, Daten, Materialien oder Weisungen Rechte " +
                    "Dritter verletzen oder gegen geltendes Recht verstoßen, stellt der Kunde den " +
                    "Anbieter von diesen Ansprüchen frei und ersetzt die erforderlichen Kosten der " +
                    "Rechtsverteidigung.\n\n" +
                    "(2) Die Freistellung gilt nicht, soweit der Anbieter die Rechtsverletzung zu " +
                    "vertreten hat.\n\n" +
                    "(3) Der Anbieter unterrichtet den Kunden unverzüglich über die Inanspruchnahme und " +
                    "stimmt sich mit ihm über die Rechtsverteidigung ab.",
                LegalNote =
                    "Die Freistellung ist an eine Pflichtverletzung des Kunden gebunden und bei " +
                    "eigenem Verschulden des Anbieters ausgenommen."
            },

            // ────────────────────────────────── Rechte und Materialien

            new()
            {
                Id = "IP_SIMPLE_LICENSE",
                Version = 1,
                SectionOrder = 600,
                Title = "Nutzungsrechte an den Arbeitsergebnissen",
                IncompatibleWith = ["IP_EXCLUSIVE_LICENSE"],
                Text =
                    "(1) Nach vollständiger Zahlung der vereinbarten Vergütung räumt der Anbieter dem " +
                    "Kunden an den für ihn erstellten Arbeitsergebnissen ein einfaches, zeitlich und " +
                    "räumlich unbeschränktes Nutzungsrecht für den vertraglich vorausgesetzten " +
                    "Verwendungszweck ein.\n\n" +
                    "(2) Bis zur vollständigen Zahlung ist die Nutzung nur widerruflich gestattet.\n\n" +
                    "(3) Weitergehende, insbesondere ausschließliche Rechte bedürfen einer gesonderten " +
                    "Vereinbarung."
            },

            new()
            {
                Id = "IP_EXCLUSIVE_LICENSE",
                Version = 1,
                SectionOrder = 600,
                Title = "Ausschließliche Nutzungsrechte an den Arbeitsergebnissen",
                IncompatibleWith = ["IP_SIMPLE_LICENSE"],
                Text =
                    "(1) Nach vollständiger Zahlung der vereinbarten Vergütung räumt der Anbieter dem " +
                    "Kunden an den für ihn erstellten Arbeitsergebnissen ein ausschließliches, zeitlich " +
                    "und räumlich unbeschränktes Nutzungsrecht für den vertraglich vorausgesetzten " +
                    "Verwendungszweck ein.\n\n" +
                    "(2) Bis zur vollständigen Zahlung ist die Nutzung nur widerruflich gestattet.\n\n" +
                    "(3) Das Recht des Anbieters, die zugrunde liegenden allgemeinen Methoden, " +
                    "Werkzeuge und Komponenten weiter zu nutzen, bleibt unberührt.",
                LegalNote =
                    "Ausschließliche Rechte sind eine kaufmännische Entscheidung und regelmäßig " +
                    "gesondert zu vergüten."
            },

            new()
            {
                Id = "PRE_EXISTING_MATERIALS",
                Version = 1,
                SectionOrder = 610,
                Title = "Rechte an Vorarbeiten, Werkzeugen und Komponenten",
                DefaultSelected = true,
                Text =
                    "(1) Allgemeine Methoden, Konzepte, Bibliotheken, Frameworks, Templates, " +
                    "Komponenten, Werkzeuge, Know-how und vorbestehender Quellcode des Anbieters " +
                    "bleiben beim Anbieter, auch wenn sie bei der Leistungserbringung eingesetzt " +
                    "werden.\n\n" +
                    "(2) Soweit solche Bestandteile in den Arbeitsergebnissen enthalten sind, erhält " +
                    "der Kunde hieran ein einfaches Nutzungsrecht im Umfang des vertraglich " +
                    "vorausgesetzten Verwendungszwecks.\n\n" +
                    "(3) Rechte Dritter an eingesetzten Komponenten richten sich nach deren " +
                    "Lizenzbedingungen."
            },

            new()
            {
                Id = "SOURCE_FILES_EXCLUDED",
                Version = 1,
                SectionOrder = 620,
                Title = "Herausgabe von Quell- und Arbeitsdateien",
                DefaultSelected = true,
                Text =
                    "(1) Geschuldet sind die fertig bearbeiteten Arbeitsergebnisse in dem für den " +
                    "vereinbarten Verwendungszweck üblichen Format.\n\n" +
                    "(2) Ein Anspruch auf Herausgabe von Rohdaten, offenen Arbeits- und Quelldateien " +
                    "oder Quellcode besteht nur, soweit dies ausdrücklich vereinbart ist."
            },

            new()
            {
                Id = "PORTFOLIO_REFERENCE",
                Version = 1,
                SectionOrder = 630,
                Title = "Referenznennung und Eigenwerbung",
                Text =
                    "(1) Der Anbieter darf das Projekt in angemessener Weise als Referenz nennen und " +
                    "im Rahmen der eigenen Außendarstellung zeigen.\n\n" +
                    "(2) Der Kunde kann dieser Nutzung jederzeit in Textform mit Wirkung für die " +
                    "Zukunft widersprechen.",
                LegalNote =
                    "Standardmäßig nicht ausgewählt. Wird nur aufgenommen, wenn die Referenznennung " +
                    "mit dem Kunden vereinbart wurde."
            },

            // ────────────────────────────────── Vertraulichkeit und Daten

            new()
            {
                Id = "CONFIDENTIALITY",
                Version = 1,
                SectionOrder = 700,
                Title = "Vertraulichkeit",
                DefaultSelected = true,
                Text =
                    "(1) Die Parteien behandeln alle im Rahmen der Zusammenarbeit erlangten " +
                    "vertraulichen Informationen und Geschäftsgeheimnisse der jeweils anderen Partei " +
                    "vertraulich und verwenden sie nur für Zwecke dieses Vertrages.\n\n" +
                    "(2) Die Pflicht gilt nicht für Informationen, die öffentlich bekannt sind, ohne " +
                    "Verletzung dieser Pflicht bekannt werden, der empfangenden Partei bereits bekannt " +
                    "waren oder aufgrund Gesetzes oder behördlicher Anordnung offenzulegen sind.\n\n" +
                    "(3) Die Parteien verpflichten Mitarbeitende und eingeschaltete Dritte " +
                    "entsprechend.\n\n" +
                    "(4) Die Pflicht besteht für die Dauer von drei Jahren nach Beendigung des " +
                    "Vertrages fort; für Geschäftsgeheimnisse gilt sie, solange deren Schutz besteht.",
                LegalNote =
                    "Der Referenzvertrag sieht eine zeitlich unbegrenzte Geheimhaltung vor. Eine " +
                    "unbegrenzte Bindung ist in AGB angreifbar; die Dauer ist anwaltlich zu bestätigen."
            },

            new()
            {
                Id = "DATA_PROCESSING_ART28",
                Version = 1,
                SectionOrder = 710,
                Title = "Datenschutz und Auftragsverarbeitung",
                DefaultSelected = true,
                Text =
                    "(1) Die Parteien beachten die jeweils geltenden datenschutzrechtlichen " +
                    "Bestimmungen.\n\n" +
                    "(2) Soweit der Anbieter personenbezogene Daten im Auftrag des Kunden verarbeitet, " +
                    "schließen die Parteien vor Beginn der betreffenden Verarbeitung einen Vertrag zur " +
                    "Auftragsverarbeitung nach Art. 28 DSGVO. Der Anbieter stellt hierfür einen " +
                    "Entwurf zur Verfügung.\n\n" +
                    "(3) Zugangsdaten werden ausschließlich über geeignete Wege übermittelt und nicht " +
                    "in diesen Vertrag aufgenommen."
            },

            new()
            {
                Id = "SUBCONTRACTORS",
                Version = 1,
                SectionOrder = 720,
                Title = "Einsatz von Subunternehmern und Drittanbietern",
                DefaultSelected = true,
                Text =
                    "(1) Der Anbieter darf zur Erbringung seiner Leistungen Subunternehmer und " +
                    "Drittanbieter einsetzen.\n\n" +
                    "(2) Für deren Leistungen bleibt der Anbieter gegenüber dem Kunden " +
                    "verantwortlich.\n\n" +
                    "(3) Datenschutzrechtliche Anforderungen an den Einsatz weiterer Auftragsverarbeiter " +
                    "bleiben unberührt."
            },

            new()
            {
                Id = "NO_LEGAL_ADVICE",
                Version = 1,
                SectionOrder = 730,
                Title = "Keine Rechts-, Steuer- oder Erfolgsberatung",
                DefaultSelected = true,
                Text =
                    "(1) Der Anbieter erbringt keine Rechts- oder Steuerberatung. Soweit er Texte, " +
                    "Hinweise oder Vorlagen bereitstellt, die rechtliche Bezüge haben, obliegt deren " +
                    "rechtliche Prüfung dem Kunden.\n\n" +
                    "(2) Die Prüfung von Kennzeichen-, Marken- und sonstigen Schutzrechten Dritter " +
                    "schuldet der Anbieter nicht."
            },

            // ────────────────────────────────── Allgemeines

            new()
            {
                Id = "FORCE_MAJEURE",
                Version = 1,
                SectionOrder = 800,
                Title = "Höhere Gewalt",
                DefaultSelected = true,
                Text =
                    "(1) Wird eine Partei durch höhere Gewalt an der Erfüllung ihrer Pflichten " +
                    "gehindert, ruhen die betroffenen Pflichten für die Dauer und im Umfang der " +
                    "Behinderung. Vereinbarte Termine verschieben sich entsprechend.\n\n" +
                    "(2) Die betroffene Partei unterrichtet die andere Partei unverzüglich.\n\n" +
                    "(3) Dauert die Behinderung länger als drei Monate an, kann jede Partei den " +
                    "betroffenen Leistungsteil in Textform kündigen."
            },

            new()
            {
                Id = "CONTACTS",
                Version = 1,
                SectionOrder = 810,
                Title = "Kommunikation und Ansprechpartner",
                DefaultSelected = true,
                Text =
                    "(1) Die Parteien benennen jeweils eine Ansprechperson für die Durchführung dieses " +
                    "Vertrages und teilen Wechsel unverzüglich mit.\n\n" +
                    "(2) Erklärungen im laufenden Projektbetrieb können in Textform, insbesondere per " +
                    "E-Mail, abgegeben werden."
            },

            new()
            {
                Id = "WRITTEN_OR_TEXT_FORM",
                Version = 1,
                SectionOrder = 820,
                Title = "Form, Vertragsänderungen und Rangfolge der Dokumente",
                DefaultSelected = true,
                RequiredFields = ["FormRequirement", "DocumentPrecedence"],
                Text =
                    "(1) Änderungen und Ergänzungen dieses Vertrages bedürfen der " +
                    "{{FormRequirement}}. Individuelle Vereinbarungen zwischen den Parteien haben " +
                    "Vorrang.\n\n" +
                    "(2) Bei Widersprüchen zwischen den Vertragsbestandteilen gilt folgende Rangfolge:\n" +
                    "{{DocumentPrecedence}}",
                LegalNote =
                    "Schriftform oder Textform ist eine bewusste Entscheidung und wird nicht geraten. " +
                    "Eine doppelte Schriftformklausel ist bewusst nicht enthalten — sie ist wegen " +
                    "§ 305b BGB angreifbar."
            },

            new()
            {
                Id = "ASSIGNMENT_SETOFF",
                Version = 1,
                SectionOrder = 830,
                Title = "Abtretung und Aufrechnung",
                Text =
                    "(1) Der Kunde kann Rechte aus diesem Vertrag nur mit vorheriger Zustimmung des " +
                    "Anbieters übertragen; die Zustimmung darf nicht unbillig verweigert werden.\n\n" +
                    "(2) Der Kunde kann nur mit unbestrittenen oder rechtskräftig festgestellten " +
                    "Forderungen aufrechnen.",
                LegalNote =
                    "Aufrechnungs- und Abtretungsbeschränkungen unterliegen der AGB-Kontrolle. " +
                    "Standardmäßig nicht ausgewählt."
            },

            new()
            {
                Id = "SEVERABILITY",
                Version = 1,
                SectionOrder = 840,
                Title = "Salvatorische Klausel",
                DefaultSelected = true,
                Text =
                    "Sollte eine Bestimmung dieses Vertrages unwirksam sein oder werden, bleibt die " +
                    "Wirksamkeit der übrigen Bestimmungen unberührt. Die Parteien werden die " +
                    "unwirksame Bestimmung durch eine wirksame ersetzen, die dem wirtschaftlichen " +
                    "Zweck der unwirksamen am nächsten kommt."
            },

            new()
            {
                Id = "GERMAN_LAW",
                Version = 1,
                SectionOrder = 850,
                Title = "Anwendbares Recht",
                DefaultSelected = true,
                Text =
                    "Auf diesen Vertrag findet das Recht der Bundesrepublik Deutschland unter " +
                    "Ausschluss des UN-Kaufrechts Anwendung."
            },

            new()
            {
                Id = "JURISDICTION_B2B",
                Version = 1,
                SectionOrder = 860,
                Title = "Gerichtsstand",
                RequiredFields = ["ProviderSeat"],
                Text =
                    "Ist der Kunde Kaufmann, juristische Person des öffentlichen Rechts oder " +
                    "öffentlich-rechtliches Sondervermögen oder hat er im Inland keinen allgemeinen " +
                    "Gerichtsstand, ist ausschließlicher Gerichtsstand für alle Streitigkeiten aus " +
                    "diesem Vertragsverhältnis {{ProviderSeat}}. Zwingende gesetzliche " +
                    "Gerichtsstände bleiben unberührt.",
                LegalNote =
                    "Die Klausel greift nur gegenüber Kaufleuten. Sie wird nicht ausgegeben, wenn der " +
                    "Sitz des Anbieters nicht als strukturierter Wert vorliegt."
            }
        ];

        /// <summary>Every module, in document order.</summary>
        public static IReadOnlyList<ClauseModule> All =>
            Modules.OrderBy(m => m.SectionOrder).ThenBy(m => m.Id, StringComparer.Ordinal).ToList();

        /// <summary>The module with this id, or null. Ids are case-sensitive.</summary>
        public static ClauseModule? Find(string id) =>
            Modules.FirstOrDefault(m => string.Equals(m.Id, id, StringComparison.Ordinal));

        /// <summary>
        /// The ids a model may choose from: everything not retired. Sent to the
        /// model as the closed list it must select within, so an invented id is
        /// recognisable as invented rather than merely unfamiliar.
        /// </summary>
        public static IReadOnlyList<string> SelectableIds =>
            Modules.Where(m => m.ReviewStatus != ClauseReviewStatus.Retired)
                   .OrderBy(m => m.Id, StringComparer.Ordinal)
                   .Select(m => m.Id)
                   .ToList();

        /// <summary>
        /// The modules that go into a contract unless somebody decides otherwise.
        /// The model's selection is added to these; it does not replace them.
        /// </summary>
        public static IReadOnlyList<string> DefaultSelectedIds =>
            Modules.Where(m => m.DefaultSelected && m.ReviewStatus != ClauseReviewStatus.Retired)
                   .Select(m => m.Id)
                   .ToList();
    }
}
