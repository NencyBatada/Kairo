import { Document, Font, Page, StyleSheet, Text, View, renderToBuffer } from "@react-pdf/renderer";
import path from "node:path";
import type { ReactNode } from "react";
import { paragraphLead, pdfText, type PdfContent } from "./pdf-content";
import { pdfTheme } from "./pdf-theme";

const fontRoot = path.join(process.cwd(), "public", "fonts", "reports");
Font.register({ family: "ReportGeist", fonts: [{ src: path.join(fontRoot, "Geist-Regular.ttf"), fontWeight: 400 }, { src: path.join(fontRoot, "Geist-SemiBold.ttf"), fontWeight: 600 }] });
Font.register({ family: "ReportSerif", src: path.join(fontRoot, "InstrumentSerif-Regular.ttf") });
Font.register({ family: "ReportHindi", fonts: [{ src: path.join(fontRoot, "NotoSansDevanagari-Regular.ttf"), fontWeight: 400 }, { src: path.join(fontRoot, "NotoSansDevanagari-SemiBold.ttf"), fontWeight: 600 }] });
Font.registerHyphenationCallback(word => [word]);

const styles = StyleSheet.create({
  page: { paddingTop: 48, paddingBottom: 60, paddingHorizontal: 44, fontFamily: "ReportGeist", fontSize: 10, color: pdfTheme.foreground, backgroundColor: pdfTheme.card },
  brand: { fontSize: 10, fontWeight: 600, color: pdfTheme.primary, marginBottom: 9 },
  title: { fontFamily: "ReportSerif", fontSize: 32, marginBottom: 12 },
  meta: { color: pdfTheme.mutedForeground, fontSize: 9, marginBottom: 5 },
  hero: { backgroundColor: pdfTheme.background, borderColor: pdfTheme.border, borderWidth: 1, borderRadius: 5, padding: 16, marginTop: 14, marginBottom: 18 },
  outcome: { fontWeight: 600, fontSize: 11, marginBottom: 6 },
  lift: { fontFamily: "ReportSerif", fontSize: 22, lineHeight: "28.6pt" },
  section: { marginBottom: 16 },
  heading: { fontWeight: 600, fontSize: 12, marginBottom: 7 },
  paragraph: { marginBottom: 7 },
  footer: { position: "absolute", left: 44, right: 44, bottom: 28, borderTopColor: pdfTheme.border, borderTopWidth: 1, paddingTop: 8, fontFamily: "ReportGeist", fontSize: 8, color: pdfTheme.mutedForeground, textAlign: "right" },
});

function runs(value: string): ReactNode {
  return pdfText(value).split(/([\u0900-\u097f\ua8e0-\ua8ff][\u0900-\u097f\ua8e0-\ua8ff\u200b\u200c\u200d]*)/u).filter(Boolean).map((text, index) => /[\u0900-\u097f\ua8e0-\ua8ff]/u.test(text) ? <Text key={index} style={{ fontFamily: "ReportHindi" }}>{text}</Text> : text);
}

export function ImpactPdfDocument({ content }: { content: PdfContent }) {
  return <Document title={content.title} author="Kairo" subject="Campaign impact report" language="en">
    <Page size="A4" style={styles.page}>
      <Text fixed style={styles.footer} render={({ pageNumber, totalPages }) => `Kairo · Impact report    ${pageNumber} / ${totalPages}`} />
      <Text style={styles.brand}>{runs(`Kairo / ${content.brand}`)}</Text>
      <Text style={styles.title}>{runs(content.title)}</Text>
      <Text style={styles.meta}>Impact report · Prepared {content.prepared}</Text>
      <Text style={styles.meta}>Campaign {content.campaignId}</Text>
      <Text style={styles.meta}>Report {content.reportId}</Text>
      <View style={styles.hero} wrap={false}><Text style={styles.outcome}>{content.outcome}</Text><Text style={styles.lift}>{content.lift}</Text></View>
      {content.sections.map((section, index) => {
        const [lead, remainder] = paragraphLead(section.paragraphs[0] || "No further details supplied.");
        return <View key={index} style={styles.section}><View wrap={false}><Text style={styles.heading}>{runs(section.title)}</Text><Text style={styles.paragraph}>{runs(lead)}</Text></View>{[remainder, ...section.paragraphs.slice(1)].filter(Boolean).map((paragraph, i) => <Text key={i} style={styles.paragraph} orphans={2} widows={2}>{runs(paragraph)}</Text>)}</View>;
      })}
    </Page>
  </Document>;
}

export async function renderImpactPdf(content: PdfContent) {
  return renderToBuffer(<ImpactPdfDocument content={content} />);
}
