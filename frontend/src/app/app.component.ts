import { Component, OnInit, inject, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { AuthService } from './core/auth.service';
import { ApiService } from './services/api.service';

@Component({
  selector: 'app-root',
  standalone: true,
  imports: [CommonModule, RouterOutlet, RouterLink, RouterLinkActive],
  templateUrl: './app.component.html',
  styleUrls: ['./app.component.css']
})
export class AppComponent implements OnInit {
  readonly auth = inject(AuthService);
  private readonly api = inject(ApiService);

  readonly apiHealthy = signal<boolean>(false);

  ngOnInit(): void {
    this.api.getHealth().subscribe({
      next: () => this.apiHealthy.set(true),
      error: () => this.apiHealthy.set(false)
    });
  }
}
