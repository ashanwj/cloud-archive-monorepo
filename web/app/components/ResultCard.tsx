import type { DocumentResponse } from '../types'
import styles from './ResultCard.module.css'

interface Props {
  result: DocumentResponse
}

export default function ResultCard({ result }: Props) {
  const formattedDate = new Date(result.createdAt).toLocaleString(undefined, {
    dateStyle: 'medium',
    timeStyle: 'short',
  })

  return (
    <div className={styles.card}>
      <div className={styles.summarySection}>
        <p className={styles.summaryLabel}>AI Summary</p>
        <p className={styles.summaryText}>{result.summary}</p>
      </div>

      <dl className={styles.meta}>
        <div className={styles.row}>
          <dt>File</dt>
          <dd>{result.fileName}</dd>
        </div>
        <div className={styles.row}>
          <dt>Document ID</dt>
          <dd className={styles.mono}>{result.documentId}</dd>
        </div>
        <div className={styles.row}>
          <dt>S3 Key</dt>
          <dd className={styles.mono}>{result.s3Key}</dd>
        </div>
        <div className={styles.row}>
          <dt>Archived</dt>
          <dd>{formattedDate}</dd>
        </div>
      </dl>
    </div>
  )
}
