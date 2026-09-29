import { Pipe, PipeTransform } from '@angular/core';

// One formatter per style; `undefined` locale = the browser's own locale and time zone.
const formats = {
  short: new Intl.DateTimeFormat(undefined, { dateStyle: 'short', timeStyle: 'medium' }),
  medium: new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'medium' }),
};

/** Formats a UTC timestamp from the API in the browser's locale and time zone. */
@Pipe({ name: 'localDate' })
export class LocalDatePipe implements PipeTransform {
  transform(value: string | Date | null | undefined, style: keyof typeof formats = 'medium'): string {
    if (!value) return '';
    const date = value instanceof Date ? value : new Date(value);
    return Number.isNaN(date.getTime()) ? '' : formats[style].format(date);
  }
}
